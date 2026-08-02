# ADR-0016: Jeder Service besitzt seine eigene DeclarativeBase (eigene MetaData)

Date: 2026-07-31
Status: Accepted
Amends: ADR-0010 (Alembic per-service, async env.py)
Related: ADR-0004 (keine geteilte Datenbank), ADR-0002 (Kernel vs. Bausteine)

## Kontext

ADR-0010 legt fest, dass jeder Service seine eigenen Migrationen besitzt, und
nennt `worker_database.Base.metadata` als `target_metadata` für Alembic-Autogenerate.
Dazu steht dort die Annahme:

> „Autogenerate vergleicht die importierten Modelle eines Service gegen
> `Base.metadata` (das für einen korrekt importierenden Service nur dessen
> Tabellen enthält), Cross-Service-Tabellen-Leakage ist also ein
> *Model-Import*-Disziplinproblem, kein Migrations-Engine-Problem."

Diese Annahme hält nicht. `worker_database.Base` ist **eine** `DeclarativeBase`
mit **einer** `MetaData`. Sobald zwei Services Modelle darauf registrieren, teilen
sie sich denselben Tabellen-Namensraum. Beim Bau des Consent-Ledgers trat das
sofort zutage: `identity-service` und `consent-service` besitzen beide — korrekt
und service-eigen (ADR-0012) — eine Tabelle `audit_events`. Im Monorepo importiert
pytest beide Modell-Module in *einem* Prozess:

```
sqlalchemy.exc.InvalidRequestError: Table 'audit_events' is already defined
for this MetaData instance.
```

Vier Testdateien des identity-service brachen bei der Collection ab. Die zweite,
gefährlichere Folge wäre stiller gewesen: Autogenerate hätte für jeden Service die
Tabellen des *anderen* als „fehlend" gesehen und `DROP TABLE` vorgeschlagen.

Die Ursache ist nicht Disziplin, sondern Topologie. Ein geteilter
Tabellen-Namensraum widerspricht direkt ADR-0004 („keine geteilte Datenbank, kein
Cross-Service-Repository"): wenn jeder Service seine Daten besitzt, muss er auch
seine `MetaData` besitzen.

## Entscheidung

**Jeder Service deklariert seine eigene `DeclarativeBase`** in seinem
`infrastructure/database`-Paket und übergibt genau deren `metadata` an Alembics
`target_metadata`.

```python
# apps/<service>/src/<module>/infrastructure/database/base.py
class Base(DeclarativeBase):
    """Declarative base whose MetaData holds only this service's tables."""
```

Die Mixins (`TimestampMixin`, `SoftDeleteMixin`, `TenantMixin`, `VersionMixin`)
bleiben in `worker_database` — sie tragen keine `MetaData` und sind echte
wiederverwendbare Bausteine (ADR-0002).

`worker_database.Base` **bleibt bestehen**, aber mit einem Docstring, der die
Falle benennt und auf das Per-Service-Muster verweist. `identity-service` nutzt es
weiterhin; da `consent-service` jetzt eine eigene `MetaData` hat, enthält
`worker_database.Base.metadata` faktisch nur noch identity-service-Tabellen und
ist damit für dessen Autogenerate wieder korrekt. Eine Migration von
identity-service auf eine eigene Base ist eine reine Umbenennung ohne
Schema-Änderung und kann jederzeit nachgezogen werden.

Eine dynamische `make_declarative_base()`-Factory wurde erwogen und **verworfen**:
ein per `type()` erzeugter Typ ist für mypy keine gültige Basisklasse
(`Invalid base class`), und eine explizite Klasse ist ohnehin lesbarer.

## Konsequenzen

- Zwei Services dürfen gleichnamige Tabellen besitzen — genau das, was
  Service-Datenhoheit bedeutet. `audit_events` existiert bewusst zweimal.
- Autogenerate sieht pro Service nur dessen Tabellen; kein versehentliches
  `DROP TABLE` mehr möglich.
- Die `worker new-service`-Templates müssen dieses Muster erzeugen (Folgearbeit;
  das aktuelle `migrations/env.py.tmpl` importiert noch `worker_database.Base`).
- Cross-Service-Fremdschlüssel sind damit auch technisch unmöglich, nicht nur
  per Konvention verboten.

## Verifikation

- `make check-py` grün: 199 passed, 20 skipped — vorher brachen vier
  identity-service-Testdateien bei der Collection ab.
- `apps/consent-service/tests/integration/test_migrations.py` prüft, dass
  `alembic upgrade head` genau `consent_events` + `audit_events` anlegt.
