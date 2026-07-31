# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Roadmap & Status

The project is run as a **staged masterplan** — read [`docs/ULTRAPLAN.md`](docs/ULTRAPLAN.md) first (foundation state, target architecture, ten phases each with a Definition of Done, risk register). [`docs/ROADMAP.md`](docs/ROADMAP.md) is the pull-through status index. ADRs in [`docs/adr/`](docs/adr/): ADR-0002 (worker-platform = kernel, worker-* = libraries), ADR-0003 (Composition-Root per service, NOT a fluent `PlatformBuilder`), ADR-0004 (versioned contracts, no scraping, consent-first). Terms in [`docs/glossary.md`](docs/glossary.md). Domain skills under [`docs/skills/`](docs/skills/) (worker-cli, consent-ledger, transfer-market) alongside opencode-specific ones in `.opencode/skill/`.

**Verified foundation state (2026-07-30):** Phase 1 (foundation, CI green) and **Phase 2 (Identity & Tenancy) are both complete**; `identity-service` has a real auth vertical slice — `POST /auth/{register,login,refresh,logout}` (bcrypt password hashing, PyJWT HS256 access/refresh tokens set as `httpOnly` cookies — ADR-0006/0007) and `GET /me` (principal resolved from the JWT via `AuthMiddleware`, tenant comes only from the JWT claim, never a header, in production). **Phase 3 (Candidate Core: Profile/Resume/Portfolio + Consent-Ledger) is in progress** (current branch `phase-3-consent-ledger`, sub-step 3.1): `apps/consent-service` has been scaffolded with the same Clean-Architecture skeleton as `identity-service` plus its own Alembic migrations dir, and the consent domain value objects (`SubjectId`, `Capability`, `ConsentEventId`, `Reason`, `ConsentAction`) are implemented — but its `compose_api.py` is still an explicit placeholder (`/health/live` only, not yet wired through `worker_platform`'s `create_api_app`). Some `worker-*` siblings are now **thin re-export layers** over the platform kernel rather than private implementations: `worker-correlation` re-exports `worker_platform.context`, `worker-config` re-exports the `worker_platform.configuration` settings family, `worker-cqrs` was deleted (use `worker_platform.application.cqrs`). `worker-health` and `worker-tenancy` remain as complementary building blocks (not duplicates). Two packages skip their smoke test with a reason: `worker-github` (source imports `from github import Github` but declared dep is `githubkit` — dependency mismatch) and `worker-files` (system `libmagic` missing for `python-magic`). Do not assume a package is production-ready because its directory exists or its heavy dependencies are installed; verify before depending. Full detail in [`docs/ROADMAP.md`](docs/ROADMAP.md).

## What this is

WorkerTransfer is a consent-first talent-mobility platform (applications, direct recruiting, employment transfers, AI-assisted career workflows). The repo is a **dual-ecosystem monorepo**: Python (`uv` workspace) and frontend (`pnpm` + `turbo`).

Two services exist today: `identity-service` (the reference, with a complete auth vertical slice) and `apps/consent-service` (early scaffold, Phase 3). A React skeleton lives in `apps/web`. `IMPLEMENTATION_PLAN.md` and `kon.txt` describe the intended 30+ package / 20+ service future state — treat them as vision, not as a description of what is implemented. The `packages/` directory already contains many `worker-*` stub packages with only an empty `__init__.py`; do not assume a package is real because its directory exists. Verify before depending on it.

## Commands

### Python (primary)
```bash
uv sync --all-packages --all-groups          # install everything (workspace + dev groups)
uv run worker-identity                        # run the reference service (port 8001)
```
Quality checks — run in this order (matches `AGENTS.md` and CI). `make check`
runs all four fail-fast; `make fix` autoremits format/import issues:
```bash
make check            # ruff format --check → ruff check → mypy → pytest
make fix              # ruff format + ruff check --fix
```

### Frontend
Single frontend test: `pnpm --filter @workertransfer/web exec vitest run src/app.test.tsx`.

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
Correlation and tenant IDs flow through `contextvars` (`worker_platform.context`), set by `CorrelationIdMiddleware` and `TenantContextMiddleware`. **Tenant identity must never come from a browser header in production** — `DevelopmentHeaderTenantResolver` is local/dev/test only (`allow_development_tenant_header` is off by default and gated on environment). See `docs/product-scope.md` for the trust constraint. In `identity-service`, `AuthMiddleware` resolves the request principal from an `Authorization: Bearer` header only; the frontend auth client instead relies on the `httpOnly` cookies set at login (`credentials: "include"`, no `Authorization` header sent) — verify that path end-to-end before building on `/me`.

### CQRS
`worker_platform.application.cqrs.Mediator` — register handlers explicitly (`register_handler`, no reflection), add pipeline behaviors (`add_behavior`, first registered = outermost). Dispatch via `await mediator.send(request)`. `Command` vs `Query` are marker subtypes of `Request`.

### Frontend
`apps/web` (Vite + React 19 + TanStack Query + TanStack Router) consumes `@workertransfer/ui` (workspace `packages/ui`, React components like `Button`/`Card`). `src/auth/client.ts` is a cookie-based auth client for `identity-service` (`/auth/login`, `/me`); `LoginResult` is a discriminated union that never throws on bad credentials. Shared TS config in `tsconfig.base.json` (strict, `noUncheckedIndexedAccess`, `verbatimModuleSyntax`, `moduleResolution: bundler`). `turbo.json` `globalEnv` includes `VITE_API_BASE_URL`. The app UI is German-language by design.

## Conventions that bite

- **Package manager is `uv`** (Python) and **`pnpm`** (frontend) — never run `pip`, `poetry`, `npm`, or `yarn`.
- **Sharing rule**: only domain-neutral, transport-independent, non-business code goes in `packages/`. Profile, company, job, transfer, contract, application, and matching models stay inside the owning service. There is no shared database and no cross-service repository abstraction. `worker-contracts` holds versioned boundary DTOs, never a shared domain model.
- **No secrets, tokens, CVs, contracts, or raw source code in the repo or in logs** (CONTRIBUTING.md, product-scope.md).
- New cross-cutting architectural decisions get an ADR in `docs/adr/`.
- Tests use `asyncio_mode = "auto"` (pytest-asyncio); no `@pytest.mark.asyncio` needed.

## Key docs

- `docs/architecture.md` — service shape, sharing rule, delivery sequence.
- `docs/product-scope.md` — consent, AI, data-acquisition, and security-by-design constraints. Read before touching anything consent- or AI-related.
- `docs/frontend.md` — frontend architecture detail.
- `docs/adr/` — architecture decision records.
- `AGENTS.md` — concise command + convention reference (mirrors much of the above).
