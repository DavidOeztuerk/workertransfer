# ADR-0011: Integration tests via Testcontainers Postgres

- **Status:** Accepted
- **Date:** 2026-07-19
- **Relates:** ADR-0002 (worker-platform kernel, worker-* libraries), ADR-0010 (Alembic per-service async), [product-scope.md](../product-scope.md), [Phase 2 design spec](../superpowers/specs/2026-07-16-phase-2-identity-tenancy-design.md) §2

## Context

The Phase-2 DoD requires "Tests für Domain + Integration (Testcontainers)". sqlite is unsuitable
for these tests: the identity schema leans on Postgres-native features that matter for tenancy and
audit correctness — `UUID`, `JSONB`, `ENUM` (`account_status`, `audit_action`), `CITEXT`
(case-insensitive email uniqueness per tenant), and timezone-aware `DateTime`. A test against sqlite
would not exercise the CITEXT case-folding (task 16 repository test asserts
`get_by_email("REPO@...")` finds the lowercase row) or the PG enum `values_callable` storage.

`testcontainers` was not installed in the Phase-1 baseline; Docker is available locally. Per the
CLAUDE.md proxy note ("do not assume a dep is installable; verify"), `testcontainers[postgres]>=4`
pinned cleanly into the root `[dependency-groups].dev` (Task 11).

## Decision

- `testcontainers[postgres]` lives in the **root** `[dependency-groups].dev` (shared across
  services, added in Task 11). Each service that needs integration tests reuses the same fixture pair.
- Integration tests live under `apps/<service>/tests/integration/`, the package marked with an
  empty `__init__.py` so the in-package `from ._docker import _docker_available` relative import
  works without a `tests/__init__.py` root package (preserving the Phase-1 collection convention
  of unique test filenames and no `tests/__init__.py`).
- **Offline-skip is green-equivalent:** a single `_docker_available()` guard powers a module-level
  `pytestmark = skipif(not _docker_available(), reason="Docker not available (ADR-0011
  offline-skip)")`. When Docker is down, the whole suite skips — it never fails an offline run.
- The Docker guard lives in a tiny `_docker.py` helper imported by both `conftest.py` and the test
  modules, so we avoid a fragile cross-package `from tests.integration.conftest import ...` (which
  would require a `tests/__init__.py` root package). The plan snippet used that cross-conftest
  import; this ADR records the cleaner relative-import path.
- **Session-scoped PG container** (`PostgresContainer("postgres:17-alpine", driver="asyncpg")`),
  URL normalized to the `postgresql+asyncpg://` driver suffix. **Per-test schema reset** via
  `Base.metadata.drop_all`/`create_all` on a fresh `AsyncEngine` for the repository roundtrips.
- **Migration correctness** is proven by a separate test that applies Alembic `upgrade head` via
  the **Alembic Python API** (`from alembic.config import Config; from alembic import command;
  command.upgrade(cfg, "head")`) — preferred for hermeticity over a `uv run alembic` subprocess
  (plan note flagged the subprocess path as fragile under the test harness). `WORKER_DATABASE_URL`
  is set in-process around the `command.upgrade` call, then restored. This proves the `0001` revision
  applies cleanly on an empty container, not just that `create_all` matches.

## Consequences

- **CI must run Docker for the integration step** (Sub-step 2.9). This ADR documents that the
  GitHub Actions `services:` block (or a `docker`-enabled runner) is required; offline
  contributor runs see skips, not red.
- **Offline green-equivalence preserved** — `make check` stays green on a machine without Docker,
  because the integration suite skips wholesale rather than failing.
- The fixture pair (`postgres_url` session-scoped + `engine`/`session_factory`/`session`
  function-scoped) is the canonical shape other `worker-*` services adopt when they grow
  integration tests.
- Testcontainers pulls `docker` (the Python SDK) as a transitive dependency; that is confined to the
  dev group and does not reach runtime images.

## Verification

- `uv run pytest apps/identity-service/tests/integration -v` → 3 tests pass when Docker is up
  (migration correctness + user-repository roundtrip + session/audit repository roundtrip).
- `make check` is green whether or not Docker is up (passes green-with-Docker, skips-green-without).
- The `_docker_available()` guard was exercised with Docker up on the dev machine (Docker 29.6.1+).
