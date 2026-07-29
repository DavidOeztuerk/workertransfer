# Phase 2 — Identity & Tenancy: Vorbereitung (Startpunkt für neue Session)

> **Status:** Branch `phase-2-identity-tenancy` @ `aa805e2` (= `develop`, Phase 1 fertig).
> Diese Datei ist ein **Startpunkt**, kein fertiger Masterplan. Phase 2 ist der
> erste echte Feature-Slice mit echter Domain, Persistenz, Sicherheit und
> Consent-Berührung — deshalb: **erst Brainstorming/Plan-Skill, dann Code.**
> Quelle: [`docs/ULTRAPLAN.md`](ULTRAPLAN.md) §Phase 2, [`docs/product-scope.md`](product-scope.md),
> [`docs/adr/`](adr/) (ADR-0002 Kanon/Bausteine, ADR-0004 Consent-First/kein Scraping).

## DoD (ULTRAPLAN)
Ein User kann sich anmelden, erhält ein JWT, Tenant kommt aus dem **Claim**,
Audit-Event ist persistiert, Identity-Service hat eine DB-Migration, Tests für
Domain + Integration (Testcontainers). CI grün (`make check`).

## Konsens-Hard-Constraints (nicht verhandelbar)
- **Tenant-Identität kommt im Produktivbetrieb aus authentifizierten Claims,
  nie aus einem Browser-Header** (`product-scope.md`, CLAUDE.md, ULTRAPLAN).
  Header-Resolver (`worker-tenancy.HeaderTenantResolver`) bleibt **nur
  local/dev/test**, gated via `allow_development_tenant_header` (default off).
- **Keine autonomen Ranking-/Kontakt-/Ablehnungsentscheidungen über Menschen.**
- **AI entwirft, Mensch entscheidet** (je rechtlicher/externer Versand).
- Keine Secrets/Tokens/CVs/Verträge im Repo oder in Logs.

## Ist-Stand (verifiziert am 16.07.2026)
- `apps/identity-service` — nur Plattform-Stub: `main.py` + `configuration.py`
  + `test_app.py` (gesunde `/health`-Tests). **Keine Domain, kein User-Aggregat,
  keine Sessions, kein Login-Endpoint, keine DB.**
- `worker-auth` — `TokenManager`, `TokenPayload` (pydantic), `hash_password`/
  `verify_password`. **Bekannte Blocker aus Phase 1**: `hash_password`/
  `verify_password` sind defekt (passlib/bcrypt-Backend kaputt:
  `AttributeError: module 'bcrypt' has no attribute '__about__'`).
  `TokenManager.__init__` speichert `algorithm` (default `RS256`);
  `create_access_token` nutzt `python-jose` mit `RS256` → braucht echten
  RSA-Schlüsselpaar. **Quelle**: Phase-1-smoke `test_smoke_worker_auth.py`
  dokumentiert diese Grenzen.
- `worker-authorization` — nur `AuthorizationService` (casbin); `__init__(model_path,
  adapter)` baut `AsyncEnforcer` → braucht casbin-Modell-Datei (.conf) auf Disk +
  DB-Adapter. Noch nicht integriert.
- `worker-database` — `Base` (DeclarativeBase), `TimestampMixin`/`SoftDeleteMixin`/
  `TenantMixin`/`VersionMixin`, `create_engine`, `create_session_factory`,
  `UnitOfWork`. **Alembic-Migrations-Workflow noch nicht etabliert** (CLI
  `worker migrate/upgrade` fehlt; sub-command-templates `infrastructure` sind
  leer — Phase-1-Folgedefizit).
- `worker-tenancy` — `HeaderTenantResolver`/`ClaimTenantResolver`/
  `SubdomainTenantResolver`/`NoTenantResolver` + eigene `_tenant_id` ContextVar
  (Typ `UUID`). `worker-middleware.TenantContextMiddleware` nutzt diese Resolver.
  **Folgedefizit aus 1.4/ADR-0005**: worker-tenancy unterhält eine eigene
  `_tenant_id`-ContextVar (`UUID | None`) **zusätzlich** zum Kanon
  `worker_platform.context._tenant_id` (`str | None`). Konsolidierung
  (UUID-vs-str-Semantik) ist **explizit Phase-2-Arbeit**, nicht Foundation.
- `worker-events` — `DomainEvent`/`IntegrationEvent` + In-Process `EventBus`
  (subscribe/publish). Für Audit-Events brauchbares Skelett, persistence fehlt.
- Frontend (`apps/web`) — React 19 + TanStack Query Skeleton, deutschsprachig.
  **Keine Auth-Route bisher** (Login/Callback = ULTRAPLAN-Ziel).

## Offene Architekturfragen (im Brainstorming zu entscheiden)
1. **Passwort-Hashing-Backend**: passlib/bcrypt ist kaputt (s.o.). Optionen:
   (a) passlib + korrigierte bcrypt-Version pinnen; (b) direktes `bcrypt`
   ohne passlib; (c) `argon2-cffi`. Empfehlung steht aus — ADR-worthy.
2. **JWT-Signatur**: `TokenManager` default `RS256` braucht RSA. Einsatz von
   `HS256` (shared secret, einfacher für ersten Slice) vs. `RS256` (asymmetrisch,
   produktionsreifer)? Schlüssel-Storage (env, Vault, K8s-secret)?
3. **OIDC vs. Eigenbau**: ULTRAPLAN sagt „OIDC/OAuth2-Einstieg". Ist das ein
   eigener Password-Flow + späterer OIDC, oder gleich via `authlib` OIDC?
4. **Tenant-Claim-Konsolidierung**: `worker-tenancy._tenant_id` (UUID) vs.
   Kanon `worker_platform.context._tenant_id` (str). Welche Richtung?
   (Empfehlung aus ADR-0005: Kanon = `worker_platform.context` — aber der
   Kanon speichert `str` und `worker-tenancy` reichere-`tenant_context`-dict
   hat der Kanon nicht. Klären, welche Semantik gewinnt.)
5. **Persistenz**: PostgreSQL via `worker-database` (SQLAlchemy 2 + asyncpg).
   Alembic-Setup + `worker migrate/upgrade` CLI — wo leben die Migrations-
   Dateien (service-lokal vs. shared)? ULTRAPLAN: „keine geteilte DB, kein
   cross-service repository". Also: Service-lokale Migration für identity-service.
6. **Testcontainers vs. leichtere**: DoD fordert „Integration (Testcontainers)".
   Docker-basiert — ist das in dieser Dev-Umgebung verfügbar? Fallback:
   `aiosqlite`/testpg, aber DoD sagt Testcontainers.
7. **Audit-Event-Modell**: Domain-Event vs. Integration-Event? Wo persistiert
   (eigene Tabelle via outbox, oder synchron)? Consent-relevant: Audit-Events
   dürfen **keine** PII aus dem Consent-Ledger preisgeben.

## Vorgeschlagene Sub-Aufgaben-Struktur (Startgerüst, Brainstorm kann anpassen)
- 2.1 Passwort-Hashing-Backend reparieren (bcrypt/argon2) + ADR.
- 2.2 `worker-auth` JWT-Ausgabe/-Verify fertigstellen (algorithm + key storage,
  access + refresh) + ADR.
- 2.3 PostgreSQL-Substrat via `worker-database` + Alembic-Migrations-Workflow
  (`worker migrate/upgrade`, service-lokal) + ADR.
- 2.4 `apps/identity-service` Domain: User-Aggregat, Account-Lifecycle, Sessions.
- 2.5 OIDC/OAuth2-Einstieg (authlib) — oder Eigenbau-Password-Flow + späterer OIDC.
- 2.6 Tenant-Kontext von Header-Resolver auf **claims-basiert** umstellen;
  `_tenant_id` UUID-vs-str Konsolidierung; Header-Resolver local/test gated.
- 2.7 `worker-authorization` integrieren (casbin RBAC/ABAC, Modell-Datei, DB-Adapter).
- 2.8 Audit-Events für sicherheitsrelevante Aktionen via `worker-events`.
- 2.9 Frontend-Auth-Flow-Einstieg (Login/Callback) als erste echte Web-Route.
- 2.10 Tests: Domain + Integration (Testcontainers); `make check` grün halten.
- 2.11 ADRs je architektonischer Entscheidung in `docs/adr/`; ROADMAP fortgeführt.

## Branch/Commit-Disziplin
- Arbeite auf `phase-2-identity-tenancy`. Commite pro Sub-Schritt (ULTRAPLAN:
  Phasen 4+ pro Schritt; Phase 2 eher pro Paket/Sub-Item).
- Keine PRs nach `main` ohne Review. `develop` ist die Integrations-Branch.
- `make check` vor jedem Commit; `mypy` strict (excludes `tests/`); ruff
  line-length 100, py314.
