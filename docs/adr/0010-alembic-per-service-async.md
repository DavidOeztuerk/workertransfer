# ADR-0010: Alembic migrations — per-service, async env.py

- **Status:** Accepted
- **Date:** 2026-07-18
- **Relates:** ADR-0002 (worker-platform kernel, worker-* libraries), ADR-0004 (no shared database, versioned contracts), [Phase 2 design spec](../superpowers/specs/2026-07-16-phase-2-identity-tenancy-design.md) §5

## Context

ULTRAPLAN §Phase 2 requires an identity-service database migration as part of its DoD.
ADR-0004 forbids a shared database and any cross-service repository abstraction — each
service owns exactly its tables. `worker-database` ships `Base` (a `DeclarativeBase`),
the `TimestampMixin` / `SoftDeleteMixin` / `TenantMixin` / `VersionMixin`, an async
`create_engine`, `create_session_factory`, and a `UnitOfWork`, but **no Alembic setup**.

The `worker-cli` `migrate` and `upgrade` commands (Phase 1.3) are thin `alembic`-subprocess
shells: they `cd` into `apps/<service>` and run `alembic …`. They fail opaquely today
because no service ships an `alembic.ini` + `migrations/` directory; the subprocess `alembic`
emits a generic error and the CLI prints "Migration creation failed" with no guidance.

SQLAlchemy 2 async (`asyncpg`) cannot run Alembic's *synchronous* default `env.py`. The
async runtime needs the `async_engine_from_config` + `connection.run_sync(do_migrations)`
pattern (`run_migrations` inside an `await connection.run_sync(...)`).

## Decision

**Per-service Alembic, async env.py.**

- Each service owns `apps/<service>/alembic.ini` and `apps/<service>/migrations/`
  (`env.py`, `script.py.mako`, `versions/`). No shared `alembic.ini`, no multi-env
  configuration — those approach the shared-database anti-pattern of ADR-0004.
- `migrations/env.py` runs **async**: `async_engine_from_config(...)` + an
  `async with engine.connect() as connection: await connection.run_sync(do_run_migrations)`
  block (the SQLAlchemy 2 async pattern). The URL comes from `WORKER_DATABASE_URL`
  (falling back to `DATABASE_URL`), never hardcoded in `alembic.ini`.
- `worker_database.Base.metadata` (the single shared `DeclarativeBase`) is the
  `target_metadata` for autogenerate. A service's models import
  `from worker_database import Base` so their tables register on that same metadata;
  `env.py` imports the service's `infrastructure.database.models` package to populate
  the metadata before `target_metadata = Base.metadata` is referenced.
- The `worker-cli` `migrate`/`upgrade` commands are repaired (Sub-step 2.2 Task 5) to
  **pre-check** `apps/<service>/alembic.ini` and emit an actionable message (referencing
  this ADR) when it is missing, instead of shelling out to a bare `alembic` failure.

## Consequences

- Migration history lives with the service that owns the tables — consistent with
  ADR-0004 and the ULTRAPLAN "no shared DB" rule.
- Autogenerate compares a service's imported models against `Base.metadata` (which, for
  a correctly-imported service, contains only that service's tables), so cross-service
  table leakage is a *model-import* discipline problem, not a migration-engine problem.
- New services scaffold with an `alembic.ini` + `migrations/` pair. The `worker-cli`
  `new-service` template should add these — recorded as a follow-up, **not** blocking
  Phase 2 (identity-service's migration is written by hand in Sub-step 2.4 Task 14).
- The sync runner for tests/CI uses `psycopg` (Alembic runs DDL synchronously); the
  runtime stays on `asyncpg`. Both coexist in the service's deps.

## Verification

Sub-step 2.4 Task 14 materially applies this: `worker migrate "init" --service identity-service`
(or the hand-written `0001_init_users_sessions_audit.py`) against a
Testcontainers Postgres (ADR-0011). Sub-step 2.4 Task 16's
`test_migrations.py` asserts `alembic upgrade head` creates the `users`, `sessions`, and
`audit_events` tables. `worker migrate/upgrade`'s missing-`alembic.ini` pre-check is
asserted by `test_migrate_reports_missing_alembic_ini` (Sub-step 2.2 Task 5).
