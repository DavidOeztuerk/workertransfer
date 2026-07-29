# Phase 2 — Identity & Tenancy: Design (Spec)

- **Status:** Draft (Brainstorming abgeschlossen, Freigabe pro Abschnitt erteilt)
- **Date:** 2026-07-16
- **Branch:** `phase-2-identity-tenancy` @ `f96df08` (geforkt von `develop`/`aa805e2`)
- **Quellen:** [`docs/ULTRAPLAN.md`](../../ULTRAPLAN.md) §Phase 2, [`docs/phase-2-prep.md`](../../phase-2-prep.md), [`docs/product-scope.md`](../../product-scope.md), [`docs/adr/`](../../adr/) (ADR-0002, ADR-0004, ADR-0005)
- **Vorgänger-ADRs:** ADR-0002 (worker-platform = Kernel, worker-* = Bausteine), ADR-0003 (Composition-Root, kein fluent Builder), ADR-0004 (Versionierte Contracts, kein Scraping, Consent-First), ADR-0005 (Kanon-Auflösung Duplikate; Tenant-Context-Konsolidierung nach Phase 2 verschoben)
- **Phase-2-ADRs (in diesem Slice entstehend):** ADR-0006 Passwort-Hashing-Backend, ADR-0007 JWT-Signing, ADR-0008 Auth-Flow (Eigenbau Password-Flow vs. OIDC), ADR-0009 Tenant-Context-Konsolidierung, ADR-0010 Alembic-Migrations-Workflow, ADR-0011 Integration-Test-Substrat (Testcontainers), ADR-0012 Audit-Event-Modell

## 1. Ziel & DoD

Phase 2 ist der erste echte vertikale Slice mit echter Domain, Persistenz, Sicherheit und
Consent-Berührung. ULTRAPLAN §Phase 2 DoD:

> Ein User kann sich anmelden, erhält ein JWT, Tenant kommt aus dem Claim, Audit-Event ist
> persistiert, Identity-Service hat eine DB-Migration, Tests für Domain + Integration
> (Testcontainers). CI grün.

Daraus abgeleitet, Phase-2-Slice-DoD (end-to-end inkl. Browser):
1. User kann sich registrieren (`POST /auth/register`) und anmelden (`POST /auth/login`) und erhält ein Access- + Refresh-JWT (HS256, PyJWT).
2. Tenant **im Produktivbetrieb** kommt aus dem authentifizierten JWT-Claim, **nie** aus einem Browser-Header (Header-Resolver nur local/test, `allow_development_tenant_header` default off; durch Integrationstest bewiesen).
3. Jede sicherheitsrelevante Aktion (register, login success/failure, token refresh/revoke) persistiert ein `AuditEvent` in derselben UoW-Transaktion (atomicity), PII-frei (nur IDs + Action + technische Metadaten).
4. Identity-Service hat eine echte PostgreSQL-Substrat + eine Alembic-Migration (`0001_init`).
5. Tests: Domain-Unit-Tests (kein Docker) + Integration-Tests (Testcontainers PostgreSQL, Alembic `upgrade head`, End-to-End).
6. Frontend `/login` (deutsch, TanStack Router, cookie-basiert) als erste echte Web-Route.
7. CI grün (`make check` = ruff format → ruff check → mypy → pytest; `.github/workflows/ci.yml` mit Docker für Testcontainers).

Hard-Constraints (nicht verhandelbar): Tenant-Identität aus authentifizierten Claims in Prod;
AI entwirft, Mensch entscheidet; keine Secrets/Tokens/CVs/Verträge/Rohquellcode im Repo oder
Logs.

## 2. Architekturentscheidungen (Brainstorming-Ergebnis)

Sieben ADRs entstehen in den Sub-Schritten, in denen die jeweilige Entscheidung umgesetzt
wird (s. §8 — so dokumentieren ADRs das Gebaute, nicht ein Phantom):

| ADR | Titel | Entscheidung | geschrieben in |
|---|---|---|---|
| 0006 | Passwort-Hashing-Backend | `bcrypt`>=4.x direkt (kein passlib); passlib-Indirection entfernt; Argon2-Cffi später addierbar | Sub-Schritt 2.1 |
| 0007 | JWT-Signing | HS256 + PyJWT, shared secret via `SecretStr`/`WORKER_`-Env; `python-jose` rausgeworfen; RS256+Vault-Storage als Phase-6/10-Upgrade-Pfad dokumentiert | 2.1 |
| 0008 | Auth-Flow | Eigenbau Password-Flow (eigenes User-Aggregat, `/register`+`/login`, access+refresh); OIDC-Provider-Flow (authlib Authorization-Code) als späterer Upgrade-Pfad | 2.3 |
| 0009 | Tenant-Context-Konsolidierung | Kanon = `worker_platform.context` (`str`-Form der UUID); `worker-tenancy` → Dünnschicht-Reexport, eigene ContextVar + dict-Speicher entfallen; `ClaimTenantResolver` scope-basiert, liest `request.state.user`; UUID-kanonische Repräsentation, ContextVar-Typ bleibt `str` (kein Runtime-Break) | 2.6 |
| 0010 | Alembic-Migrations-Workflow | Pro-Service `alembic.ini` + `apps/<service>/migrations/` (async `env.py` via `async_engine_from_url`); `Base` aus `worker-database` als autogenerate-Target; `worker migrate/upgrade` repariert | 2.4 |
| 0011 | Integration-Test-Substrat | Testcontainers PostgreSQL (`testcontainers[postgres]` dev-dep), session-scoped Fixture, Alembic `upgrade head`, async session/UoW, skip-if-no-docker | 2.4 |
| 0012 | Audit-Event-Modell | Eigenes `AuditEvent` in identity-service Domain; eigene `audit_events`-Tabelle, synchron persistiert in derselben UoW-Transaktion; In-Process EventBus-Publish; `metadata` = validated Allowlist (keine PII); Outbox-Upgrade = Phase 9 | 2.7 |

### Begründungsauszüge

- **bcrypt direkt (ADR-0006):** `passlib[bcrypt]` ist kaputt (`AttributeError: module 'bcrypt' has no attribute '__about__'` — passlib ungepflegt seit ~2023, inkompatibel mit `bcrypt`>=4.x). Keine migrationwürdigen Hashes vorhanden → keine Hash-Migration. `bcrypt`>=4.x direkt ist wenig Abhängigkeit, aktiv gepflegt, De-facto-Industriestandard. Argon2-Cffi ist später als zusätzliche Scheme ergänzbar, ohne Singled-Scheme-Lock-in.
- **HS256+PyJWT (ADR-0007):** `python-jose` ist effektiv tot (letztes Release 2022, ungepatchte CVEs). PyJWT aktiv gepflegt, schon als dep deklariert. Für einen monorepo-internen Identity-Service, dessen Tokens nur von worker-eigenen Services in gleicher Trust-Domäne konsumiert werden, reicht HS256 (shared secret). RS256 (asymmetrisch) zahlt sich erst bei externen Validators / trust boundaries (Phase 6/10) aus; sauberer Upgrade-Pfad dokumentiert. Schlüssel-Storage `SecretStr` via `WORKER_IDENTITY_JWT_SECRET` (Runtime Secret, nie im Repo; dev/test-Default nur im `LOCAL`/`TEST`-Env).
- **Eigenbau Password-Flow (ADR-0008):** ULTRAPLAN §Phase 3 baut die Consent-Ledger *auf das Profil* — also brauchen wir eine eigene Identität, an der Consent hängt (`User.tenant_id` als Consent-Zukunftsträger). Ein externer IdP widerspräche dem Domain-first-Ziel (kein User-Aggregat im Repo). OIDC als *Provider* (authlib Authorization-Code + `/callback`) ist ~3x der Scope ohne daß wir heute OIDC-Provider sein wollen → späterer Upgrade-Pfad.
- **Tenant-Kanon (ADR-0009):** ADR-0005 sagt Kanon = `worker_platform.context` (läuft, getestet). Verifiziert: zwei *inkompatible* Resolver-Signaturen (Platform: `resolve(scope)->str|None` gespeichert als `str`; worker-tenancy: `resolve(request)->UUID|None` eigener `_tenant_id: UUID` + `tenant_context`-dict). Auflösung: Platform bleibt Source-of-Truth; worker-tenancy wird Dünnschicht-Reexport (wie `worker-correlation`/`worker-config`); `ClaimTenantResolver` liest `request.state.user` (via Auth-Middleware gesetzt), liefert UUID-as-str ins Platform-Kontextvar; eigene ContextVar + dict-Speicher entfallen. UUID-kanonische Repräsentation, aber ContextVar-Typ bleibt `str` (kein Runtime-Break in der laufenden Platform; `SubdomainTenantResolver`/`DevelopmentHeaderTenantResolver` parse已经有了). Eine ContextVar, eine scope-basierte Resolver-Signatur.
- **Pro-Service Alembic (ADR-0010):** ULTRAPLAN + ADR-0004: keine geteilte DB, kein cross-service repository. Jede Service-eigene DB → Service-lokale Migration. `Base` aus `worker-database` als gemeinsames `DeclarativeBase` autogenerate-Target (Models im Service importieren `Base`). Async `env.py` via `async_engine_from_url`-Pattern (SQLAlchemy 2).
- **Testcontainers (ADR-0011):** DoD fordert Testcontainers. Docker 29.6.1 läuft, testcontainers nicht installiert → als dev-group-dep hinzufügbar. sqlite ist untauglich (UUID/JSONB/ENUM/timezone-spezifische PG-Features sind für Tenancy & Audit relevant). skip-if-no-docker für Offline-Läufe.
- **Eigenes AuditEvent, sync UoW (ADR-0012):** Audit treibt keine Aggregatzustandswechsel (kein klassisches Domain-Event) und geht nicht via Broker (kein Integration-Event) → eigener `AuditEvent`-Typ in identity-service Domain (service-spezifischer Payload; ADR-0004: Business-Entities im owning Service). Synchron persistiert in derselben UoW-Transaktion wie der Command = atomicity (login + audit-Write = alles-oder-nichts). In-Process `EventBus` (worker-events) zum Publish; ein audit-Handler subskribiert + persistiert. Outbox (async via Broker) = ULTRAPLAN Phase 9. **PII-Regel:** Audit speichert nur IDs (actor_id, tenant_id, target_id) + Action + technische Metadaten (`metadata` = validated Allowlist: `reason`, `ip`, `user_agent`); **niemals** Passwort, Email, Consent-Payload, Tokens.

## 3. Architektur-Überblick & Schichten

Clean Architecture, inward-pointing: Presentation → Application → Domain; Infrastructure
implementiert Application-Ports. Domain kennt weder FastAPI noch SQLAlchemy noch JWT.

```
apps/identity-service
├─ src/identity_service/
│  ├─ domain/
│  │   ├─ entities/user.py          User-Aggregat (root), Account-Lifecycle-State
│  │   ├─ value_objects/            Email, PasswordHash, UserId, TenantId (UUID)
│  │   ├─ events.py                 UserRegistered / UserLoggedIn / AuditEvent
│  │   └─ services/                 PasswordHashing-VB-Port (Domain-Interface)
│  ├─ application/
│  │   ├─ commands/                 RegisterUser, AuthenticateUser, RefreshToken, RevokeToken
│  │   ├─ queries/                  (trivialer /me)
│  │   ├─ ports.py                  UserRepository, AuditRepository, SessionRepository,
│  │   │                             TokenService-Port, Hashing-Port, Clock-Port
│  │   └─ mediator.py               register_handler pro Command; add_behavior (audit/logging)
│  ├─ infrastructure/
│  │   ├─ database/models.py        SQLAlchemy-Modelle: users, sessions, audit_events
│  │   ├─ database/repositories.py  SqlAlchemy-Implementierungen der Ports
│  │   ├─ auth/                     JwTokenService (PyJWT, HS256), BcryptPasswordHasher, SystemClock
│  │   └─ compose.py                Composition-Root: UoW, repos, TokenService, hasher, EventBus, mediator
│  ├─ presentation/
│  │   ├─ http/router.py           /auth/register, /auth/login, /auth/refresh, /auth/logout, /me
│  │   ├─ auth_middleware.py        JWT-Verify, setzt request.state.user (User DTO)
│  │   └─ compose_api.py           build_app(settings) — nutzt Platform create_api_app + compose-hook
│  │                              (s. §6: create_api_app(settings, *, routers, tenant_resolver, auth_middleware?))
│  ├─ configuration.py             IdentityServiceSettings (+ JWT_SECRET, DATABASE_URL, ...)
│  └─ main.py                      build_app(settings) ruft compose_api
├─ migrations/                      alembic.ini, env.py (async), versions/0001_init_*.py
└─ tests/
    ├─ unit/                        Domain + Application-Tests (kein Docker)
    └─ integration/                 Testcontainers pg-Fixture, alembic upgrade head, End-to-End
```

Bausteine (worker-*, kernel-frei, via Composition-Root angebunden — ADR-0002/0003):
- `worker-auth` **repariert** (ADR-0006/0007): bcrypt-direct, PyJWT HS256; `python-jose` raus.
- `worker-database` **erweitert** (ADR-0010): Alembic service-lokales Setup; `Base` = autogenerate-Target.
- `worker-events` **genutzt** (ADR-0012): In-Process `EventBus` für `AuditEvent`-Publish; keine Persistenz im shared Package.
- `worker-tenancy` **konsolidiert** (ADR-0009): Dünnschicht-Reexport des Platform-Kanons; `ClaimTenantResolver` liest `request.state.user`.
- `worker-platform` **erweitert** (ADR-0009): `create_api_app` bekommt einen compose-hook (`routers`, `tenant_resolver`, `auth_middleware`), damit der Service claims-basierten Tenant-Resolver & Auth-Middleware registriert, ohne daß der Kernel Business-Logik lernt (ADR-0002: Kernel klein).

Frontend (`apps/web`): `/login`-Route-Modul (TanStack Router), deutsche UI, form → `POST /auth/login`, access/refresh in HTTP-only cookies, tenant aus claim, TanStack Query-Plain-Client mit cookie-auth.

## 4. Domain-Modell

### User-Aggregat (root, `worker-core.Entity`)
```
class User(Entity):
    id: UserId            (UUID ValueObject)
    tenant_id: TenantId   (UUID ValueObject) — fix bei register, nie null; 1 User = 1 Tenant (Phase 2)
    email: Email          (ValueObject: lowercased + validiert; equality per lowercased form)
    password_hash: PasswordHash   (ValueObject: wrappt str; nie plaintext; Factory nur via Hashing-Port)
    display_name: str
    status: AccountStatus (Enum: PENDING → ACTIVE → SUSPENDED → DISABLED)
    roles: tuple[str, ...] (immutable)
    created_at, updated_at, version (optimistic concurrency)
```
- Kein Plain-Password-Feld im Aggregat; `password` lebt nur transitional im Command, wird sofort gehasht.
- Account-Lifecycle (Transitions validiert im Aggregat): Phase 2 aktiviert synchron `ACTIVE` bei register (keine Email-Verifizierung in Phase 2; später). `ACTIVE → SUSPENDED → ACTIVE`; `→ DISABLED`. Login nur erfolgreich bei `status == ACTIVE`.
- Login bei `SUSPENDED`/`DISABLED` → `AccountDisabled`-DomainError (invalid transition / not-allowed), **AuditEvent(LOGIN_FAILURE) trotzdem persistiert**. Falsches Passwort / unbekannte Email → `InvalidCredentials`-DomainError (ebenfalls mit `AuditEvent(LOGIN_FAILURE)`). Beide mappen HTTP-seitig einheitlich auf 401 (s. §7).
- `tenant_id`: fix mitgebracht bei `register` (1 User = 1 Tenant in Phase 2). Wird Claim-Inhalt des JWT.

### Value-Objects (`worker-core.ValueObject`, immutable, validierend)
- `Email` — lowercased, RFC-ish-Regex; equality lowercased.
- `PasswordHash` — wrappt `str`; Factory nur via `PasswordHashing`-Port.
- `UserId` / `TenantId` — UUID-Wrapper.

### Domain-Services
- `PasswordHashing` (Domain-Interface): `hash(plain) -> PasswordHash`, `verify(plain, hashed) -> bool`. Implementation `BcryptPasswordHasher` in `infrastructure/auth/` (ADR-0006).

### Domain-Events (`worker-events.DomainEvent`-Subtypen)
- `UserRegistered(event_id, user_id, tenant_id, email, occurred_at)` — **Email ist PII** → ins Audit *nicht* (s. AuditEvent).
- `UserLoggedIn(user_id, tenant_id, jti, occurred_at)` — kein Passwort, kein Email.
- `AuditEvent(actor_id, tenant_id, action, target_id?, occurred_at, metadata: dict)` (ADR-0012). `action` ∈ {`REGISTER`, `LOGIN_SUCCESS`, `LOGIN_FAILURE`, `TOKEN_REFRESH`, `TOKEN_REVOKE`}. `metadata` = validated Allowlist non-PII-Keys (`reason`, `ip`, `user_agent`); Konstruktion mit verbotenem Key wirft. `actor_id` bei unbekannter Email (`LOGIN_FAILURE`) = `NULL`.

## 5. Persistenz & Migrationen

Drei Tabellen, via `worker-database.Base` + Mixins, in `infrastructure/database/models.py`:

```
users (UserModel: Base, TimestampMixin, VersionMixin)
    id            UUID PK (default uuid4)
    tenant_id     UUID NOT NULL indexed
    email         citext/text NOT NULL, UNIQUE (tenant_id, email)   # unique pro tenant
    password_hash text/varchar NOT NULL
    display_name  varchar NOT NULL
    status        accountstatus-PG-enum NOT NULL default 'active'
    roles         JSONB array of varchar NOT NULL default '[]'
    # kein SoftDelete in Phase 2 (Lifecycle DISABLED / spätere GDPR-Löschphase)

sessions (SessionModel: Base, TimestampMixin)   # refresh-token jti-Ledger
    id            UUID PK
    user_id       UUID FK→users.id NOT NULL indexed ondelete CASCADE
    tenant_id     UUID NOT NULL indexed
    refresh_jti   varchar NOT NULL UNIQUE
    expires_at    timestamptz NOT NULL
    revoked_at    timestamptz NULL
    # /refresh prüft jti gegen diese Tabelle (nicht revoked, nicht expired).
    # Erzeugt bei login; revoked bei logout/refresh-Rotation (Rotation: alte revoke, neue anlegen).

audit_events (AuditEventModel: Base, TimestampMixin)
    id            UUID PK
    actor_id      UUID NULL indexed                    # NULL bei LOGIN_FAILURE unbekannter Email
    tenant_id     UUID NOT NULL indexed
    action        audit-action-enum NOT NULL
    target_id     UUID NULL
    correlation_id varchar NULL                        # aus platform-context
    occurred_at   timestamptz NOT NULL
    metadata      JSONB NOT NULL default '{}'          # PII-allowlist app-side enforced
```
- PG-spezifische Typen (`UUID`, `JSONB`, `ENUM`, `citext`) — mit Testcontainers-PG testbar (deshalb sqlite ausgeschlossen).
- `email UNIQUE (tenant_id, email)` — zwei Tenants dürfen denselben `user@…`-String haben (claims-basiertes Multi-Tenant).
- FK `sessions.user_id ondelete CASCADE` (User-Löschung → sessions weg, GDPR-consistent). `audit_events` **nicht** kaskadiert (Aufbewahrungspflicht; Anonymisierung später).
- SoftDelete weggelassen (Phase 2: physisch / via Lifecycle `DISABLED`).

Alembic (ADR-0010, pro-Service, async):
- `apps/identity-service/alembic.ini` — `script_location = migrations`; URL von ENV (`WORKER_DATABASE_URL`/`DATABASE_URL`) überschrieben.
- `migrations/env.py` — async via `async_engine_from_url`-Pattern; importiert `Base.metadata` als autogenerate-Target.
- `migrations/versions/0001_init_users_sessions_audit.py` — erste Revision (hand-written; Folge-Revisionen autogenerate via `worker migrate`).
- `worker-cli migrate/upgrade` **repariert**: verifiziert `apps/<service>/alembic.ini`, führt `alembic revision --autogenerate`/`alembic upgrade head` in `apps/<service>` aus.

Testcontainers-Fixture (ADR-0011, dev-group `testcontainers[postgres]`):
- `tests/integration/conftest.py` — `@pytest.fixture(scope="session") postgres_container()` startet `PostgresContainer("postgres:17-alpine")`, skip-if-no-docker (`pytest.importorskip("testcontainers")` + Docker-Daemon-Check).
- `db_url` + async `engine` + `session_factory` + `apply_migrations()` (Alembic `upgrade head`) + `uow`.
- Jeder Integrationstest bekommt frische DB (truncate/per-container-recreation) → atomicity/independence. End-to-End: register → login (Audit geloggt) → refresh → /me, gegen echte PG.

## 6. Auth-Kern, Endpoints, Middleware

`worker-auth`-Reparatur (ADR-0006/0007, kernel-frei):
```
worker_auth/
├── password.py    BcryptPasswordHasher (bcrypt>=4.x direkt): hash_password/verify_password (constant-time via bcrypt), PasswordTooLong/PasswordHashError
├── jwt/token.py   TokenManager (PyJWT, HS256): create_access_token / create_refresh_token / verify_token(*,expected_type)
├── jwt/payload.py TokenPayload (pydantic): sub, tenant_id (UUID→str), roles, permissions, exp, iat, type, jti
└── __init__.py    Re-Exports
```
- `python-jose` aus deps raus; `bcrypt>=4.0,<5.0` addieren; `pyjwt` bleibt. HS256 secret via `WORKER_IDENTITY_JWT_SECRET` (`SecretStr`, ≥32 Bytes prod; dev/test-Default nur `LOCAL`/`TEST`, nie committed).

Infrastructure `JwTokenService` (`infrastructure/auth/`) adaptiert `worker_auth.TokenManager` an den Domain-`TokenService`-Port (Application kennt nicht PyJWT); erzeugt `TokenPair {access, refresh}`.

HTTP-Endpoints (`presentation/http/router.py`):
```
POST /auth/register   {email, password, display_name, tenant_id}     # tenant_id aus request (Bootstrap, s.u.)
    → RegisterUserCommand → User (synchron ACTIVE) → AuditEvent(REGISTER)
    → returns {user_id, tenant_id}  (keine Credentials im Response)
POST /auth/login      {email, password}
    → AuthenticateUserCommand → verify PW + status==ACTIVE → TokenPair
    → persists Session(refresh_jti) + AuditEvent(LOGIN_SUCCESS)
    → on failure: AuditEvent(LOGIN_FAILURE, actor_id=NULL wenn user unbekannt), 401 generic
POST /auth/refresh    (refreshToken aus cookie)
    → RefreshTokenCommand → jti in sessions valid (nicht revoked, nicht expired)
    → rotate: alte session.revoke, neue session + neuer TokenPair + AuditEvent(TOKEN_REFRESH)
POST /auth/logout
    → revoke current session.refresh_jti + AuditEvent(TOKEN_REVOKE)
GET  /me              Authorization: Bearer <access>
    → claims → {user_id, tenant_id, roles}
```
- `register.tenant_id`: in Phase 2 **nicht aus authentifiziertem Kontext** (man ist nicht eingeloggt bei register) → aus request (Bootstrap). Phase 3+ bringt Tenant-Provisioning via Admin-Flow. `tenant_id` ist Pflicht, kein blinder Default.
- Cookies: `access` (HTTP-only, Secure in prod, SameSite=Strict, `__Host-`-prefix in prod) + `refresh` (HTTP-only, Secure, SameSite=Strict, Path=`/auth/refresh|/auth/logout`). **Nie** localStorage.
- `/me` demonstriert claims-basierten Tenant-Read — `request.state.user.tenant_id` → `worker_platform.context.get_tenant_id()`.

Auth-Middleware (`presentation/auth_middleware.py`):
- Liest `Authorization: Bearer <access>`, verifiziert via `JwTokenService`, setzt `request.state.user = AuthPrincipal(user_id, tenant_id, roles)`.
- Bei fehlendem/ungültigem Token → `request.state.user = None` (keine Exception; Endpoints entscheiden selbst; geschützte → 401).
- Stack-Reihenfolge (Starlette outer-last): `CorrelationId` (outer) → `AuthMiddleware` → `TenantContextMiddleware(claim-based)` → `SecurityHeaders` (inner).

Tenant-Switch (ADR-0009):
- `create_api_app(settings, *, routers, tenant_resolver, auth_middleware?)`: Kernel bekommt compose-Hook (default weiter `NoTenantResolver` + health-only). Identity-Service übergibt `ClaimTenantResolver` + Auth-Router + Auth-Middleware.
- `worker-tenancy.ClaimTenantResolver` auf `scope`-Signatur umgestellt, liest `request.state.user` (ASGI: `scope["state"]["user"]`), liefert `tenant_id` als `str` ins Platform-Kontextvar; eigene `_tenant_id`-ContextVar + `tenant_context`-dict entfallen; worker-tenancy wird Dünnschicht-Reexport um Platform-`get_tenant_id`/`tenant_context`.
- Produktivbetrieb: `allow_development_tenant_header=False` default → nur claims, nie Header. Local/test: `DevelopmentHeaderTenantResolver` bleibt verfügbar (gated).

## 7. Fehler, Sicherheit, Consent-Abgrenzung

Fehler-Modell (RFC 9457 via `worker_platform.presentation.errors`; Domain-Errors ≠ HTTP-Errors — beabsichtigte Schichtentrennung):
- `UserAlreadyExists` → 409 (email pro tenant). Enumeration in Kauf genommen bei register (nicht die sensitive Failure-Fläche).
- Domain: `InvalidCredentials` (falsches PW / unbekannte Email) und `AccountDisabled` (`SUSPENDED`/`DISABLED`) sind **distinct** DomainErrors, **aber HTTP-Oberfläche wirft einheitlich 401** (`InvalidCredentials` als HTTP-Einheitsantwort) — **enumeration-safe**: gleiche Response für alle drei Fälle. **AuditEvent unterscheidet** `LOGIN_FAILURE` + `metadata.reason` (forensisch, interne Sicht).
- `TokenRefreshFailure` → 401.

Sicherheits-Festschreibungen:
- Tenant aus claims, nie Browser-Header in prod (`allow_development_tenant_header=False` default; Header-Resolver nur `LOCAL`/`DEV`/`TEST`).
- Keine Secrets/repo/logs — JWT-Secret runtime-only (`SecretStr`); bcrypt-Hashes in DB, nie plaintext, nie gelogged; `AuthenticateUserCommand.password` transient (niemals correlation-/body-logging).
- **Rate-Limiting nicht in Phase 2** (`worker-ratelimit`-Baustein existiert; Integration kommt Hardening-Phase). TODO-Marker im router; ADR/ROADMAP dokumentieren "vor External-Exposure"-Empfehlung.
- Security Headers weiter via `SecurityHeadersMiddleware`; `enforce_https`/HSTS in prod.
- `register`-Password-Policy via `worker-security` adoptiert **sofern** vorhanden, sonst mini-Domain-Validierung (Mindestlänge etc.) — Fallback.

Consent-Abgrenzung (Phase 3 = Consent-Ledger; Phase 2 legt nur Identität):
- Phase 2 hat **kein** Consent-Feature; Consent-Ledger ist ULTRAPLAN Phase 3.
- `AuditEvent`-PII-Regel setzt vorausschauend: `metadata` allowlist, keine Consent-Payloads → in Phase 3 werden Consent-Events das Audit nicht PI-verunreinigen (ADR-0012).
- `User.tenant_id` = Consent-Zukunftsträger (Phase 3 indiziert pro `(tenant_id, user_id)`). Phase 2 endet mit `TenantId` als stabilem Claim → Consent in Phase 3 hat authentifizierte Grundlage.
- Nicht in Phase 2: Profil-Sichtbarkeit, Document-Attachment, Employer-Contact, GitHub-Import (alle Consent-gated, Phase 3+).

`make check`-Disziplin: pro Sub-Schritt ruff format → ruff check → mypy (strict, excludes tests) → pytest; nur grün committen. Neue identity-Domain-Dateien: volle Annotation, `Result`/`DomainError`-Typung, kein `Any`-Schleichweg. Frontend parallel: `pnpm check` (tsc) + `pnpm test` vor Frontend-Commit.

CI `.github/workflows/ci.yml`: Docker aktivieren für Testcontainers-Integrationtests; ruff/mypy/pytest-Reihenfolge bleibt; Frontend-Schritte bleiben.

## 8. Testing-Strategie

**Unit** (`tests/unit/`, kein Docker) — Domain + Application, pure:
- `User`-Aggregat: Status-Transitions, `UserRegistered`-Event-Auslösung, `Email`-Validierung (case-insensitive, ungültige Formen), `PasswordHash`-Kapselung.
- `BcryptPasswordHasher`: hash→verify roundtrip, wrong-password→False, too-long→Error, bcrypt-Format (`$2b$`/`$2y$`).
- `TokenManager` (HS256): access-encode→decode roundtrip, expired→`ExpiredToken`, wrong-type→`InvalidToken`, tampered-signature→`InvalidToken`, `jti`-uniqueness.
- Commands gegen Mock-Ports (Fake-Repos, Clock): happy-path + alle DomainErrors incl. failed-login-with-Audit-write.
- Audit-PII-rule: `AuditEvent.metadata` akzeptiert nur allowlist keys; verbotener Key → Exception.

**Integration** (`tests/integration/`, Testcontainers-PG):
- Bootstrap: container up, Alembic `upgrade head`, Engine/Session/UoW-Fixture.
- `register` persistiert User; `UNIQUE(tenant_id, email)`-Verletzung → 409.
- `login` happy-path → persists Session(refresh_jti) + AuditEvent(LOGIN_SUCCESS) in **derselben Transaktion** (atomicity-assert via rollback-on-failure Probe).
- `login` failure (falsches PW, unbekannte Email, disabled) → 401 generic + AuditEvent(LOGIN_FAILURE, actor_id=NULL für unbekannte) persisted.
- `refresh` rotation: alte Session `revoked_at` gesetzt, neue angelegt; refresh mit revoked jti → 401.
- End-to-End via `TestClient`+ echte PG: `/register→/login→/me→/refresh→/logout`; `request.state.user.tenant_id` == claims; `get_tenant_id()` == claimed tenant.
- **Tenant-Source-assertion**: Test beweist, daß im Produktivbetrieb (`allow_development_tenant_header=False`) ein `X-Tenant-ID`-Header **ignoriert** wird und tenant nur aus dem JWT kommt (Hard-Constraint-DoD).

Smoke-Tests pro geräumtes Paket bleiben unangetastet außer `worker-auth`-Smoke (blockiert nicht mehr; aktualisiert).

## 9. Sub-Schritt-Reihenfolge (Ansatz A: innen → außen)

Pro Sub-Schritt: `make check` grün davor, commit danach.

1. **2.1** `worker-auth` reparieren: bcrypt-direct + PyJWT (HS256); `python-jose` raus; smoke grün. → ADR-0006, ADR-0007
2. **2.2** `worker-database` Alembic-Setup (shared `Base` als autogenerate-Target; generic async-`env.py`-Helper, service-lokal verdrahtet). → ADR-0010 (Teil)
3. **2.3** identity-service **Domain**: User-Aggregat, ValueObjects, Events, AuditEvent, `PasswordHashing`-Port + Unit-Tests. → ADR-0008, ADR-0012 (Teil)
4. **2.4** identity-service **Persistenz**: Modelle, Repos, `infrastructure/auth` (JwTokenService, Bcrypt-Hasher-Adapter), `compose.py` Composition-Root; erste Migration `0001`; Testcontainers-Fixture. → ADR-0010, ADR-0011
5. **2.5** identity-service **Application + HTTP**: Commands, `/register`/`/login`/`/refresh`/`/logout`/`/me`, Auth-Middleware, `worker-security`-Password-Policy (fallback).
6. **2.6** **Tenant-Konsolidierung + claims-Switch** (ADR-0009): `worker-tenancy`→Reexport, `ClaimTenantResolver` scope-basiert liest `request.state.user`; `create_api_app` bekommt compose-hook; identity-service auf claims umgestellt; Tenant-Source-assertion-Test. → ADR-0009
7. **2.7** **Audit-via-EventBus**-Verdrachtung (ADR-0012 vollständig: command→AuditEvent→EventBus.publish→Audit-Handler→persist) + Audit-Integration-Tests. → ADR-0012
8. **2.8** **Frontend `/login`** (deutsch, TanStack Router, cookie-basiert, tenant aus claim, TanStack Query-Plain-Client); `pnpm check`/`pnpm test` grün.
9. **2.9** **ROADMAP** Phase-2-Eintrag pflegen; ADRs verlinken; docs/glossary ggf. ergänzen; final `make check` + `pnpm check`/`pnpm test`; CI-Docker schalten.

## 10. DoD-Abdeckung

| ULTRAPLAN §Phase 2 DoD | abgedeckt durch |
|---|---|
| User kann sich anmelden | 2.5 (`/auth/login`) |
| erhält ein JWT | 2.1/2.5 (HS256+PyJWT, access+refresh) |
| Tenant kommt aus dem Claim | 2.6 + Tenant-Source-assertion-Test (`allow_development_tenant_header=False`) |
| Audit-Event persistiert | 2.7 (sync UoW) |
| Identity-Service hat DB-Migration | 2.4 (`0001_init`) |
| Tests Domain + Integration (Testcontainers) | 2.3/2.4/2.7 |
| CI grün | 2.9 |
| Frontend-Auth-Flow-Einstieg | 2.8 (`/login`) |
