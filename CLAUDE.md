# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Roadmap & Status

The project is run as a **staged masterplan** — read [`docs/ULTRAPLAN.md`](docs/ULTRAPLAN.md) first (foundation state, target architecture, ten phases each with a Definition of Done, risk register). [`docs/ROADMAP.md`](docs/ROADMAP.md) is the pull-through status index. ADRs in [`docs/adr/`](docs/adr/): ADR-0002 (worker-platform = kernel, worker-* = libraries), ADR-0003 (Composition-Root per service, NOT a fluent `PlatformBuilder`), ADR-0004 (versioned contracts, no scraping, consent-first). Terms in [`docs/glossary.md`](docs/glossary.md). Domain skills under [`docs/skills/`](docs/skills/) (worker-cli, consent-ledger, transfer-market) alongside opencode-specific ones in `.opencode/skill/`.

**Verified state (2026-07-31).** Phases 1, 2 and 2.5 are complete; **Phase 3 sub-step 3.1 (Consent-Ledger) is in progress**.

`identity-service` is the reference and the only fully-wired service: `POST /auth/{register,login,refresh,logout}` (bcrypt, PyJWT HS256, tokens delivered as `httpOnly` cookies — ADR-0006/0007) and `GET /me`, with the tenant coming only from the JWT claim in production. `AuthMiddleware` accepts the token from **either** an `Authorization: Bearer` header **or** the `access` cookie — the browser only ever has the cookie.

`apps/consent-service` is roughly 10% built: workspace scaffold, Alembic skeleton (`migrations/versions/` is still **empty**) and the domain value objects. Its `presentation/compose_api.py` is an explicit placeholder that constructs a bare `FastAPI()` and bypasses `create_api_app` — so it has no correlation IDs, no security headers and no problem-details. **Do not copy it as the pattern**; copy `identity-service`.

Some `worker-*` siblings are **thin re-export layers** over the kernel, not private implementations: `worker-correlation` → `worker_platform.context`, `worker-config` → the `worker_platform.configuration` settings family, `worker-logging` → `worker_platform.logging`. Deleted as duplicates: `worker-cqrs` (ADR-0005), `worker-middleware` (ADR-0009), `worker-security` and `worker-exceptions` (ADR-0014). `worker-health` and `worker-tenancy` are complementary building blocks, not duplicates.

Caveats worth knowing before you depend on anything:
- **`worker-github` is unimportable** — its source does `from github import Github` (PyGithub) while the declared dep is `githubkit`. Its smoke test skips.
- **`worker-ai` and `worker-files` are excluded from the uv workspace** (`pyproject.toml` `[tool.uv.workspace] exclude`) because their ML/C wheels have no Python-3.14 build; they are also excluded from mypy.
- Most `worker-*` packages have exactly one smoke test and **no production consumer**. A directory existing proves nothing — verify before depending.

Full detail, including the Phase-2.5 findings, is in [`docs/ROADMAP.md`](docs/ROADMAP.md).

## What this is

WorkerTransfer is a consent-first talent-mobility platform (applications, direct recruiting, employment transfers, AI-assisted career workflows). The repo is a **dual-ecosystem monorepo**: Python (`uv` workspace, 34 packages) and frontend (`pnpm` + `turbo`).

Two Python services exist: `identity-service` and `consent-service`. A React app lives in `apps/web` (two routes: `/` and `/login`). The vision documents in [`docs/vision/`](docs/vision/) (`kon.txt`, `IMPLEMENTATION_PLAN.md`) describe a 30+ package / 21 service future state — **treat them as intent, not as description**. One rule from them is load-bearing and easy to lose: *no microservice is written before the platform can generate it* — new services should come out of `worker new-service`, not out of copy-paste.

## Commands

### Everything at once
```bash
docker compose up --build # Postgres + every service + the web app, migrations included
docker compose down       # stop (add -v to drop the databases)
make check                # the full six-step gate (Python then frontend), fail-fast
make fix                  # ruff format + ruff check --fix
```
`docker compose up` is the whole local stack — there is no companion script. Each
service container migrates itself on start (`docker/entrypoint.sh`), so a fresh
clone needs no manual `alembic` or `psql`. Source is bind-mounted and both
services run under `--reload`, so host edits apply without a rebuild; rebuild
only when a dependency changes. **Adding a service:** add its database to
`scripts/initdb/`, copy a service block in `docker-compose.yml` and change four
values — no new Dockerfile (`docker/service.Dockerfile` is shared and takes
`SERVICE_DIR` as a build arg). `docker/web.Dockerfile` runs Vite.

### Python
```bash
uv sync --all-packages --all-groups   # install everything (workspace + dev groups)
uv run worker-identity                # reference service (port 8001)
make check-py                         # ruff format --check → ruff check → mypy → pytest
```
Single test / target:
```bash
uv run pytest packages/worker-platform/tests/test_cqrs.py
uv run pytest apps/identity-service -k cookie
```
mypy **excludes `tests/`** (strict everywhere else) and the two excluded packages. ruff ignores `S101`/`S105`/`S106`/`S603`/`S607` under `**/tests/**`.

### Frontend
```bash
pnpm install
make check-web        # = pnpm check (tsc --noEmit) + pnpm test (Vitest)
pnpm dev              # Vite dev server (turbo --parallel)
pnpm build
```
Single frontend test: `pnpm --filter @workertransfer/web exec vitest run src/app.test.tsx`.

### CI
`.github/workflows/ci.yml` has **two** jobs: `backend-quality` (uv sync --locked → ruff format → ruff check → mypy → pytest, Python 3.14) and `frontend-quality` (pnpm install --frozen-lockfile → check → test → build, Node 24). `ubuntu-latest` has Docker, so the Testcontainers integration suites really run there; without a daemon they self-skip (ADR-0011).

## Architecture

### Layering (Clean Architecture, inward-pointing dependencies)
```
Presentation  ->  Application  ->  Domain
     |                              ^
     └-------- Infrastructure ------┘
```
- **Domain** (`worker-core`: `Entity`, `ValueObject`, `DomainEvent`, `Result`, `DomainError`) has zero FastAPI/ORM/transport dependency.
- **Application/Presentation cross-cutting** lives in `worker-platform`: typed `PlatformSettings` (pydantic-settings, `WORKER_` env prefix), JSON logging, context propagation, async CQRS `Mediator` with ordered `PipelineBehavior`s, health probes, RFC 9457 problem errors, security headers.
- **Infrastructure** implements application ports (DB repos, message transport, storage) — not yet built.

### Every service follows the same shape
`apps/identity-service` is the fully-wired reference. A service has its own `pyproject.toml`, depends on `worker-core` + `worker-platform`, defines a service-specific `PlatformSettings` subclass, and calls `worker_platform.presentation.app.create_api_app(settings)` to get a FastAPI app wired with correlation IDs, tenant context, security headers, exception handlers, and health routes. Service entrypoints are exposed as `[project.scripts]` console commands (e.g. `worker-identity`). `apps/consent-service` follows the same directory shape but hasn't been wired through `create_api_app` yet — don't copy its `compose_api.py` as the pattern to follow.

Each service owns per-service async Alembic migrations under `apps/<service>/migrations/` (ADR-0010) and integration tests under `apps/<service>/tests/integration/` that spin up a session-scoped `testcontainers` Postgres and self-skip when Docker isn't available (`ADR-0011 offline-skip` — `make check`/CI stay green either way; GitHub Actions' `ubuntu-latest` has Docker, so they actually run there).

### Request context (important)
Correlation and tenant IDs flow through `contextvars` (`worker_platform.context`), set by `CorrelationIdMiddleware` and `TenantContextMiddleware`. **Tenant identity must never come from a browser header in production** — `DevelopmentHeaderTenantResolver` is local/dev/test only (`allow_development_tenant_header` is off by default and gated on environment). See `docs/product-scope.md` for the trust constraint.

**A tenant is a company, and a natural person has none** (ADR-0017). Tenant is an *optional* attribute of a principal, carried only by company-based features (job ads, employer accounts, recruiting teams). It is not the scoping axis for personal data: user data is scoped by user/subject identity, and both axes coexist. The consent-ledger therefore has no `tenant_id` on `consent_events` by design — a consent belongs to the person and follows them across employers. Do not "fix" that by adding a tenant column.

Company membership is a relation (`user_tenant_memberships`), not a column on `users` — one person may act for several companies (ADR-0018). Email is globally unique. `POST /auth/login` returns a person token with **no** tenant claim; `POST /auth/tenant/{id}` verifies membership and only then mints a token carrying `tenant_id`. So the client names the company but the server decides, and the tenant in the token never came from client input. `AuthPrincipal.tenant_id`, `TokenPayload.tenant_id`, `sessions.tenant_id` and `audit_events.tenant_id` are all nullable, and `None` means "acted as a person" — not "missing". Creating memberships is deliberately not exposed over HTTP; that belongs to the company-service that does not exist yet.

`AuthMiddleware` resolves the principal from an `Authorization: Bearer` header **or** the `access` cookie, in that order. Both carriers are needed: service-to-service and CLI callers send the header; the browser never sees the `httpOnly` token and can only replay it as a cookie (`credentials: "include"`). Any new service verifying identity-service tokens must accept both.

### CQRS
`worker_platform.application.cqrs.Mediator` — register handlers explicitly (`register_handler`, no reflection), add pipeline behaviors (`add_behavior`, first registered = outermost). Dispatch via `await mediator.send(request)`. `Command` vs `Query` are marker subtypes of `Request`.

### Frontend
`apps/web` (Vite + React 19 + TanStack Query + TanStack Router) consumes `@workertransfer/ui` (workspace `packages/ui` — currently only `Button` and `Card`, hand-written CSS with `--wt-*` custom properties; no Tailwind, no Radix, no component library). `src/auth/client.ts` is the cookie-based client for `identity-service`; `LoginResult` is a discriminated union that never throws on bad credentials. `src/auth/session.ts` exposes `useSession()` / `useLogout()` — the single source of "am I logged in". Shared TS config in `tsconfig.base.json` (strict, `noUncheckedIndexedAccess`, `verbatimModuleSyntax`, `moduleResolution: bundler`). `turbo.json` `globalEnv` includes `VITE_API_BASE_URL`.

The UI is German, but **hardcoded** — there is no i18n layer, and one German string (`"Anmeldung fehlgeschlagen"`) lives in the API client. Tests assert the German literals directly, so introducing i18n later means touching them.

## Conventions that bite

- **Package manager is `uv`** (Python) and **`pnpm`** (frontend) — never run `pip`, `poetry`, `npm`, or `yarn`.
- **Sharing rule**: only domain-neutral, transport-independent, non-business code goes in `packages/`. Profile, company, job, transfer, contract, application, and matching models stay inside the owning service. There is no shared database and no cross-service repository abstraction. `worker-contracts` holds versioned boundary DTOs, never a shared domain model.
- **No secrets, tokens, CVs, contracts, or raw source code in the repo or in logs** (CONTRIBUTING.md, product-scope.md).
- **Python 3.14 required** (`.python-version`) — and it must be a *final* 3.14, not an rc. pydantic passes `prefer_fwd_module` to `typing._eval_type`, which 3.14.0rc2 rejects; every pydantic model with a `UUID` field then fails to build. ruff: `line-length=100`, `target-version=py314`, selects `E F I B UP ASYNC RUF S`.
- **Declare workspace dependencies in `[project.dependencies]`**, not only `[tool.uv.sources]` — the latter tells uv *where* to resolve a name, it installs nothing. `tests/test_workspace_dependencies.py` enforces this.
- New cross-cutting architectural decisions get an ADR in `docs/adr/`.
- Tests use `asyncio_mode = "auto"` (pytest-asyncio); no `@pytest.mark.asyncio` needed.
- New services come from `worker new-service <name>` (Composition-Root + Alembic + Testcontainers scaffolding), not from copying an existing service.

## Key docs

- `docs/architecture.md` — service shape, sharing rule, delivery sequence.
- `docs/product-scope.md` — consent, AI, data-acquisition, and security-by-design constraints. Read before touching anything consent- or AI-related.
- `docs/frontend.md` — frontend architecture detail.
- `docs/adr/` — architecture decision records.
- `AGENTS.md` — concise command + convention reference (mirrors much of the above).
