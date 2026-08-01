# ADR-0017: Tenant ist ein Unternehmensbegriff — natürliche Personen haben keinen

Date: 2026-08-01
Status: Accepted
Related: ADR-0009 (Tenant-Kontext im Kernel), ADR-0013 (Consent-Ledger), ADR-0012 (Audit in-UoW), [product-scope.md](../product-scope.md)

## Kontext

Der Begriff *Tenant* war implementiert, aber nie definiert: das Glossar kannte
ihn nicht, `product-scope.md` sagte nur, woher er **nicht** kommen darf. In der
Folge wurde er als „gehört zu jedem Prinzipal" gelesen und überall
mitgeschleift — `users.tenant_id` ist `NOT NULL`, `AuthPrincipal.tenant_id` ist
nicht optional, und `RegisterUserCommand` verlangt ihn.

Beim Review von Phase 3 fiel auf, dass `consent_events` **keine** `tenant_id`
hat und der Ledger nicht mandantengetrennt liest. Ob das ein Fehler ist, ließ
sich aus dem Code nicht beantworten, weil die Produktbedeutung von Tenant
nirgends stand.

## Entscheidung

**Ein Tenant ist ein Unternehmen.** Er existiert für unternehmensbasierte
Funktionen: Stellenausschreibungen veröffentlichen, Arbeitgeberkonten,
Recruiting-Teams, und alles Weitere, das eine Organisation als handelnde Einheit
hat.

**Eine natürliche Person hat keinen Tenant.** Kandidatinnen und Kandidaten
brauchen weder Mandant noch Organisationszugehörigkeit, um die Plattform zu
nutzen. Ein Tenant ist damit ein *optionales* Attribut eines Prinzipals, kein
Pflichtfeld.

**Mandantentrennung ersetzt keine Nutzertrennung.** Dass eine Person keinen
Tenant hat, heißt ausdrücklich **nicht**, dass ihre Daten ungetrennt geladen
werden. Jeder Lese- und Schreibpfad bleibt personenbezogen gescopet — im
Consent-Ledger über `subject_id`, anderswo über die Nutzer-Identität. Tenant ist
eine **zusätzliche** Achse für Unternehmensdaten, keine Ersatzachse für
Nutzerdaten.

**Der Consent-Ledger ist subjekt-, nicht mandantengescopet.** Eine Einwilligung
gehört der Person, die sie erteilt, und folgt ihr — unabhängig davon, bei
welchem Unternehmen sie sich bewirbt oder ob sie später den Arbeitgeber
wechselt. `consent_events` hat deshalb **absichtlich keine** `tenant_id`, und
`ConsentEventRepository` filtert korrekt allein über `subject_id` und
`capability`. Eine Mandantenspalte hier wäre nicht nur überflüssig, sie wäre
falsch: sie würde nahelegen, dieselbe Person könne pro Unternehmen
unterschiedlich eingewilligt haben.

**`audit_events.tenant_id` im consent-service bleibt und ist meistens `NULL`.**
Phase 3 erzwingt Selbstverwaltung (`actor_id == subject_id`), und der Actor ist
eine Person — also ohne Tenant. Die Spalte wird trotzdem aus dem Kontext
befüllt statt hartkodiert, damit ein späteres Delegationsmodell (ein
Unternehmen handelt für eine Person) die Zuordnung ohne Migration mitschreibt.
`NULL` ist dort die richtige Antwort, kein fehlender Wert.

## Konsequenzen

- Der Ledger braucht **keine** Migration. Der beim Review vermutete Defekt war
  keiner; es fehlte die Begründung, nicht die Spalte.
- **`identity-service` wurde angeglichen** — siehe ADR-0018. Beim Verfassen
  dieser ADR war Tenant dort noch verpflichtend und kam zusätzlich aus dem
  Request-Body von `/auth/register` und `/auth/login`, was neben dieser ADR auch
  `product-scope.md` widersprach (ein Body ist genauso client-kontrolliert wie
  ein Header). Aufgelöst durch eine Mitgliedschafts-Relation, global eindeutige
  E-Mail und einen verifizierten Tenant-Wechsel.
- `ClaimTenantResolver` ist bereits korrekt: fehlt der Claim, liefert er `None`,
  statt zu scheitern. Am Kernel ist nichts zu ändern.
- Neue Services entscheiden bewusst, ob eine Tabelle eine Tenant-Achse braucht.
  Die Vorgabe ist: Unternehmensdaten ja, personenbezogene Daten nein.

## Verifikation

- `apps/consent-service/tests/unit/test_audit_tenant.py` — der Tenant wird aus
  dem Kontext übernommen, fehlt er, ist er `None`, und ein defekter Claim
  verhindert das Aufzeichnen der Consent-Tatsache nicht.
- `apps/consent-service/tests/integration/test_repository_roundtrip.py` —
  Consent wird allein über `subject_id`/`capability` gelesen.
