# Phase 3, Sub-step 3.1 — Consent-Ledger Implementation Plan

> **For agentic workers:** Execute this plan task-by-task. Use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Consent-Ledger as a standalone, append-only service (`apps/consent-service`). GRANT/REVOKE/DELETE via HTTP, synchronous `/check` projection from the event log, audit atomicity per ADR-0012, HS256-JWT trust from identity-service. No shared DB; no cross-service imports.

**Architecture:** Clean Architecture mirror of `identity-service`: Presentation → Application → Domain ← Infrastructure. `worker-platform` kernel, `worker-auth`/`worker-database`/`worker-events`/`worker-contracts` composable libraries wired via Composition-Root (ADR-0002/0003). Slice order = domain-first (inside→out).

**Tech Stack:** Python 3.14 (uv workspace), FastAPI, SQLAlchemy 2 async + asyncpg, Alembic, PyJWT 2.x (trust from identity-service JWT), pydantic 2, pytest + `testcontainers[postgres]`.

**Spec:** [`docs/superpowers/specs/2026-07-26-phase-3-substep-3.1-consent-ledger-design.md`](specs/2026-07-26-phase-3-substep-3.1-consent-ledger-design.md)

**Branch:** `phase-3-consent-ledger` (off `origin/develop`).

---

## Global Constraints

- **`make check` before every commit** (fail-fast order: `ruff format --check` → `ruff check` → `mypy packages apps` → `pytest`). `make fix` for format/import autogen.
- Python 3.14 required; ruff `line-length=100`, `target-version=py314`, selects `E F I B UP ASYNC RUF`.
- No `pip`/`poetry`; Python = `uv`; no `npm`/`yarn`; frontend = `pnpm`.
- Commit per sub-step. No PRs to `main`; target = `develop`.
- No secrets in repo. JWT secret is runtime-only (`SecretStr`).

---

## File Structure (locked decomposition)

```
packages/worker-contracts/src/worker_contracts/
├── consent.py                   NEW — ConsentGrantV1, ConsentRevokeV1, ConsentCheckV1, ConsentStateV1

packages/worker-contracts/tests/
├── test_smoke_worker_contracts.py MODIFY — add Consent DTO smoke tests

apps/consent-service/
├── pyproject.toml               NEW — deps: worker-core, worker-platform, worker-shared,
│                                       worker-auth, worker-database, worker-events, worker-contracts,
│                                       fastapi, uvicorn, sqlalchemy[asyncio], asyncpg, alembic,
│                                       psycopg[binary]; dev: httpx, testcontainers[postgres]
├── alembic.ini                  NEW — alembic config (mirror identity-service)
├── migrations/
│   ├── env.py                   NEW — async env.py
│   ├── script.py.mako           NEW
│   └── versions/
│       └── 0001_init_consent.py NEW — consent_events + audit_events schema
├── src/consent_service/
│   ├── __init__.py
│   ├── main.py                  NEW — entrypoint (uvicorn.run via worker-consent script)
│   ├── configuration.py         NEW — ConsentServiceSettings(PlatformSettings)
│   ├── domain/
│   │   ├── __init__.py
│   │   ├── consent_event.py     NEW — ConsentEvent aggregate (append-only)
│   │   ├── value_objects.py     NEW — SubjectId, Capability, ConsentEventId, Reason
│   │   ├── services.py          NEW — project_state(events) reine Funktion
│   │   └── ports.py             NEW — ConsentEventRepository, Clock, UnitOfWork
│   ├── application/
│   │   ├── __init__.py
│   │   ├── commands.py          NEW — Grant/Revoke/Delete/Check commands + handlers
│   │   └── mediator.py          NEW — pro-service Mediator
│   ├── infrastructure/
│   │   ├── __init__.py
│   │   ├── compose.py           NEW — Composition-Root
│   │   ├── clock.py             NEW — SystemClock
│   │   ├── database/
│   │   │   ├── __init__.py
│   │   │   ├── models.py        NEW — ConsentEventModel, AuditEventModel
│   │   │   └── repositories.py  NEW — SqlAlchemyConsentEventRepository, SqlAlembicAuditRepository
│   ├── presentation/
│   │   ├── __init__.py
│   │   ├── compose_api.py       NEW — build_app(settings) hook
│   │   ├── auth_middleware.py    NEW — JWT auth middleware (HS256 verify, trust identity-service)
│   │   └── http/
│   │       ├── __init__.py
│   │       └── router.py        NEW — POST /consent/grant|revoke|delete|check
├── tests/
│   ├── __init__.py
│   ├── conftest.py              NEW — Testcontainers-Postgres fixture (session-scoped)
│   ├── _docker.py               NEW — docker availability guard
│   ├── test_app.py              NEW — /health/live endpoint
│   ├── unit/
│   │   ├── __init__.py
│   │   ├── test_consent_event.py NEW — GRANT/REVOKE/DELETE construction + metadata allowlist
│   │   ├── test_projection.py   NEW — project_state() pure function
│   │   └── test_value_objects.py NEW — SubjectId, Capability, Reason
│   └── integration/
│       ├── __init__.py
│       ├── test_migrations.py   NEW — alembic upgrade head
│       ├── test_repository_roundtrip.py NEW — append/stream/latest_effective
│       ├── test_consent_endpoints.py NEW — HTTP grant/revoke/delete/check
│       ├── test_audit_atomicity.py NEW — audit persist/rollback
│       └── test_eventbus_seam.py NEW — domain-event publish
```

Root changes:
```
pyproject.toml                    MODIFY — add consent-service to uv.workspace.members
uv.lock                           MODIFY — regenerated after consent-service deps resolve
docs/ROADMAP.md                    MODIFY — Phase-3 status 3.1 entry
docs/adr/0013-consent-ledger-standalone.md NEW — ADR-0013
```

---

## Tasks

### Task 0 — Scaffolding & Workspace

- [ ] 3.1.0.1 `apps/consent-service/pyproject.toml` mit allen Deps
- [ ] 3.1.0.2 Root `pyproject.toml`: `members = [..., "apps/consent-service"]` + `consent-service = { workspace = true }` in sources
- [ ] 3.1.0.3 `uv sync --all-packages --all-groups` → lock frisch
- [ ] 3.1.0.4 `alembic.ini` + `migrations/env.py` + `migrations/script.py.mako`
- [ ] 3.1.0.5 `configuration.py` = `ConsentServiceSettings(PlatformSettings)` (Port 8002, eigenes DB-URL)
- [ ] 3.1.0.6 `main.py` mit `create_app()` + `run()` entrypoint; Script `worker-consent` registrieren
- [ ] 3.1.0.6 Verify: `uv run worker-consent` startet (startet mal ohne DB, /health/live 200)
- Commit: `feat(consent): scaffold consent-service workspace + skeleton`

### Task 1 — Domain: Value Objects

- [ ] 3.1.1.1 `domain/session_objects.py`: `SubjectId(UUID)`, `Capability(str, regex invalid)`, `ConsentEventId(UUID)`, `Reason(str, max-length, optional-at-grant, required-at-revoke/delete)`, `ConsentAction(StrEnum GRANT|REVOKE|DELETE)`
- [ ] 3.1.1.2 `tests/unit/test_value_objects.py` — valid + grenzwertälle pro VALUE-Objekt
- [ ] `make check` grün
- Commit: `feat(consent-domain): value objects SubjectId/Capability/ConsentEventId/Reason`

### Task 2 — Domain-Projection (reine Funktion)

- [ ] 3.1.2.1 `services.py`: `project_state(events: Sequence[ConsentEvent]) → ConsentState`. Regeln:
  - GRANT nach leer → aktiv (granted=True)
  - GRANT → REVOKE → deaktiviert (granted=False, reason)
  - GRANT → REVOKE → GRANT → aktiv (Re-Consent)
  - DELETE nach GRANT → logisch gelöscht (granted=False, deleted=True)
  - Duplicate-GRANT (gleiches recorded_at) → event_id DESC Tiebreaker
  - Kein Event → granted=False
  - REVOKE ohne Grant → invalid-action → Exception
- [ ] 3.1.2.2 `tests/unit/test_projection.py` — alle Regeln explizit testen
- [ ] `make check` grün
- Commit: `feat(consent-domain): project_state pure function`

### Task 3 — Domain: ConsentEvent (Aggregat, append-only)

- [ ] 3.1.3.1 `consent_event.py`: `ConsentEvent` dataclass (frozen, slots). Konstruktion über `classmethod` `grant()`/`revoke()`/`delete()`. Metadaten-Allowlist: nur `{"reason","ip","user_agent","actor_id"}` — und nicht erlaubte Keys wirft `ConsentMetadataError`. `actor_id` optional. Keine `email`/`password`/`tokens` im Metadata-Body → test.
- [ ] 3.1.3.2 `tests/unit/test_consent_event.py` — GRANT/REVOKE/DELETE Konstruktionen + MetadataError bei PII/Unbekanntem
- [ ] `make check` grün
- Commit: `feat(consent-domain): ConsentEvent aggregate (append-only)`

### Task 4 — Domain-Ports

- [ ] 3.1.4.1 `ports.py`: `ConsentEventRepository(Protocol)`: `append`, `stream(subject)`, `latest_effective(subject, capability)`. `Clock` Protocol. 
- [ ] `make check` grün
- Commit: `feat(consent-domain): domain ports`

### Task 5 — Database-Migration + SQLAlchemy Modelle

- [ ] 3.1.5.1 `migrations/versions/0001_init_consent.py` — Schema: consent_events (id, event_id UUID unique, No for subject_id, capability, action check constraint, actor_id nulle, reason, metadata jsonb, recorded_at) + audit_events (id, actor_id nullable, tenant_id, action, target_id, correlation_id, occurredDid, metadata). Indizes: ux_consent_events_event_id (unique), ix_consent_lookup (sub_id, cap, recorded_at DESC, event_id DESC).
- [ ] 3.1.5.2 `infrastructure/database/models.py` — `ConsentEventModel`, `AuditEventModel` (SQLAlchemy, von identity-service gespiegelt)
- [ ] 3.1.5.3 `tests/integration/test_migrations.py` — `alembic upgrade head` via Testcontainers → consent_events + audit_events existieren; check constraint valid
- [ ] `make check` grün
- Commit: `feat(consent-db): 0001 migration + models`

### Task 6 — Repository + Clock-Dapter

- [ ] 3.1.6.1 `infrastructure/database/repositories.py`: `SqlAlchemyConsentEventRepository`: `append` (INSERT), `stream` (SELECT alle Events per subject ORDER BY recorded_at), `latest_effective` (SELECT DISTINCT ON (subject_id, capability) ... — Tabelle s Postgres mit DESC-Sortierung)
- [ ] 3.1.6.2 `infrastructure/clock.py`: `SystemClock` (UTC)
- [ ] 3.1.6.3 `tests/integration/test_repository_roundtrip.py` — append→stream→latest_effective; idempotenz (event_id unique check); GRANT→REVOKE→GRANT Projektion
- [ ] `make check` grün
- Commit: `feat(consent-infra): repository + clock`

### Task 7 — Application Commands & Mediator

- [ ] 3.1.7.1 `application/commands.py`:
    - `GrantConsentCommand`/`RevokeConsent-Dto`/`DeleteAcceptanceDTO` → Command DTOs
    - `handle_grant`, `handle_revoke`, `handle_delete` — handler-Funktionen (wie identity-service) mit UoW
    - `CheckConsentQuery` + `handle_check` (Read-only, kein UoW)
- [ ] 3.1.7.1 Der `har-command-Med-iator` referenziert identity-service (deps: dict, repos: dict). Grant/Revoke/Delete persistieren Consent-Event + synchronen Audit-Aufruf in derselben UoW. Check handler ruft `latest_effective` auf.
- [ ] 3.1.7.2 `application/mediator.py` — pro-service Mediator (Die-Anfaklik-Kon)
- [ ] `make check` grün
- Commit: `feat(consent-application): commands, handlers, mediator`

### Task 8 — Infrastructure Composition

- [ ] 3.1.8.1 `infrastructure/compose.py` → Identity-service-Spiegel: `request_scope` context-manager, `compose_infrastructure()` → engine/client-factory/hasher/tokens/clock/repos EventBus
- [ ] No `worker-templates` Abhängigkeit; auth-middleware von identity-service konsumiert JWT (identisch Shared-Secret consistency)
- [ ] `make check` grün
- Commit: `feat(consent-infra): composition`

### Task 9 — Presentation (HTTP)

- [ ] 3.1.9.1 `presentation/http/router.py` + `auth_middleware.py` (JWT verify via identity-service shared secret; JWT token = identity-service access token)
- [ ] Enpoints: `POST /consent/grant`, `POST /consent/revoke`, `POST /consent/delete`, `POST /consent/check` (Query)
- [ ] 3.1.9.1 `presentation/compose_api.py` → `build_app()` (CORS-hook, auth-middleware, router via Platform-`create_api_app`)
- [ ] `make check` grün
- [ ] `tests/integration/test_consent_endpoints.py` — grant/revoke/Check-Monad via `TimelineTestClient` + correct auth
- [ ] `tests/integration/test_audit_atomicity.py` — `ConsentMetadataError` → kein Audit; erfolgreich = Audit da
- [ ] `tests/integration/test_eventbus_seam.py` — Domain-Events werden publisht
- Commit: `feat(consent-http): REST endpoints`

### Task 10 — Cross-Contract DTOs

- [ ] 3.1.10.1 `packages/worker-contracts/src/worker_contracts/__init__.py` → `ConsentGrantV1`, `ConsentRevokeV1`, `ConsentCheckV1`, `ConsentStateV1` (Pydantic DTOs)
- [ ] 3.1.10.2 `tests/smoke_worker_contracts.py` — auf-DTO-Stabilität (keine field changes)
- [ ]`make check` grün
- Commit: `feat(contracts): Consent DTOs V1`

### Task 11 — App-Test + Final Gate

- [ ] 3.1.11.1 `tests/test_app.py` → `/health/live` status_code=200, X-Correlation-ID present, security headers
- [ ] 3.1.11.2 `make check` → ruff = 0, mypy packages apps = 0, pytest = all green + skipped (offline skip für Docker)
- [ ] `docs/adr/0013-consent-ledger-standalone.md` → ADR-0013 schreiben
- [ ] `docs/ROADMAP.md` → `### Phase 3 — Step 3.1` entry update
- [ ] `docs/ULTRAPLAN.md` → Phase-3 consent-Vermerk
- [ ] Alles commit und Reviewer-baseline
- Commit: `feat(consent-ledger): final integration gate + ADR-0013`

---

## DoD (Sub-step 3.1)

- `apps/consent-service` startet via `worker-consent` Console-Script, `/health/live antwortet`
- `alembic upgrade head` erstellt `consent_events` + `audit_events` (`test_migrations.py`)
- `POST /consent/grant|revoke|delete` schreibt append-only + Audit atomic; `POST /consent/check` projektiert; revocation sofort sichtbar
- PII-Allowlist erzwungen (`test_consent_event.py`); Domain-Event-Publish-Seam getestet
- `worker-contracts` DTOs V1 versioniert; Stability Smoke-Test
- ADR-0013 geschrieben
- `make check` grün (ruff + mypy + pytest); ROADMAP Entry gesetzt