# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Roadmap & Status

The project is run as a **staged masterplan** — read [`docs/ULTRAPLAN.md`](docs/ULTRAPLAN.md) first (foundation state, target architecture, ten phases each with a Definition of Done, risk register). [`docs/ROADMAP.md`](docs/ROADMAP.md) is the pull-through status index. ADRs in [`docs/adr/`](docs/adr/): ADR-0002 (worker-platform = kernel, worker-* = libraries), ADR-0003 (Composition-Root per service, NOT a fluent `PlatformBuilder`), ADR-0004 (versioned contracts, no scraping, consent-first). Terms in [`docs/glossary.md`](docs/glossary.md). Domain skills under [`docs/skills/`](docs/skills/) (worker-cli, consent-ledger, transfer-market) alongside opencode-specific ones in `.opencode/skill/`.

**Verified foundation state (2026-07-16):** `worker-platform`, `worker-core`, `identity-service`, `worker-cli`, ~30 `worker-*` libraries, and `apps/web` all exist with real implementations. **Phase 1 (foundation) is complete — all six sub-tasks done and the 4-gate CI is green** (1.1 ruff→0, 1.2 mypy→0 in 54 sources, 1.3 `worker` CLI repaired & smoke-tested, 1.4 architectural duplicates resolved per ADR-0005, 1.5 smoke-tests per active package: 34 files / 65 tests total, 63 passed + 2 skipped, 1.6 premerge wrapper). Phase-1 gating runs via `make check` (`Makefile`: ruff format-check → ruff check → mypy → pytest, fail-fast) plus `make fix` / `make lint` / `make type` / `make test`; `.github/workflows/ci.yml` runs the same order. Each active `worker-*` package now has `tests/test_smoke_<pkg>.py` (import + one pure-kernel call, no external services). Two packages skip with a reason: `worker-github` (source imports `from github import Github` but declared dep is `githubkit` — dependency mismatch) and `worker-files` (system `libmagic` missing for `python-magic`). Some `worker-*` siblings are now **thin re-export layers** over the platform kernel rather than private implementations: `worker-correlation` re-exports `worker_platform.context`, `worker-config` re-exports the `worker_platform.configuration` settings family, `worker-cqrs` was deleted (use `worker_platform.application.cqrs`). `worker-health` and `worker-tenancy` remain as complementary building blocks (not duplicates). Do not assume a package is production-ready because its directory exists or its heavy dependencies are installed; verify before depending. Full detail in [`docs/ROADMAP.md`](docs/ROADMAP.md). Next: Phase 2 (Identity & Tenancy).

## What this is

WorkerTransfer is a consent-first talent-mobility platform (applications, direct recruiting, employment transfers, AI-assisted career workflows). The repo is a **dual-ecosystem monorepo**: Python (`uv` workspace) and frontend (`pnpm` + `turbo`).

Only the **foundation** exists today: `worker-core`, `worker-platform`, the `identity-service` reference service, and a React skeleton in `apps/web`. `IMPLEMENTATION_PLAN.md` and `kon.txt` describe the intended 30+ package / 20+ service future state — treat them as vision, not as a description of what is implemented. The `packages/` directory already contains many `worker-*` stub packages with only an empty `__init__.py`; do not assume a package is real because its directory exists. Verify before depending on it.

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
# equivalent explicit steps:
uv run ruff format --check .
uv run ruff check .
uv run mypy packages apps
uv run pytest
```
Run a single test / target:
```bash
uv run pytest packages/worker-platform/tests/test_cqrs.py::MediatorTests
uv run pytest tests/test_app.py -k name
```
mypy **excludes `tests/`** (strict everywhere else). ruff ignores `S101` (assert) in `**/tests/**`.

### Frontend
```bash
pnpm install
pnpm check    # tsc --noEmit (turbo, all workspaces)
pnpm test     # Vitest (turbo)
pnpm dev      # Vite dev server (turbo --parallel)
pnpm build
```
Single frontend test: `pnpm --filter @workertransfer/web exec vitest run src/app.test.tsx`.

### CI
`.github/workflows/ci.yml` runs `uv sync --locked` then ruff format check → ruff check → pytest → `mypy packages apps` on Python 3.14.

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
`apps/identity-service` is the reference. A service has its own `pyproject.toml`, depends on `worker-core` + `worker-platform`, defines a service-specific `PlatformSettings` subclass, and calls `worker_platform.presentation.app.create_api_app(settings)` to get a FastAPI app wired with correlation IDs, tenant context, security headers, exception handlers, and health routes. Service entrypoints are exposed as `[project.scripts]` console commands (e.g. `worker-identity`).

### Request context (important)
Correlation and tenant IDs flow through `contextvars` (`worker_platform.context`), set by `CorrelationIdMiddleware` and `TenantContextMiddleware`. **Tenant identity must never come from a browser header in production** — `DevelopmentHeaderTenantResolver` is local/dev/test only (`allow_development_tenant_header` is off by default and gated on environment). See `docs/product-scope.md` for the trust constraint.

### CQRS
`worker_platform.application.cqrs.Mediator` — register handlers explicitly (`register_handler`, no reflection), add pipeline behaviors (`add_behavior`, first registered = outermost). Dispatch via `await mediator.send(request)`. `Command` vs `Query` are marker subtypes of `Request`.

### Frontend
`apps/web` (Vite + React 19 + TanStack Query) consumes `@workertransfer/ui` (workspace `packages/ui`, React components like `Button`/`Card`). Shared TS config in `tsconfig.base.json` (strict, `noUncheckedIndexedAccess`, `verbatimModuleSyntax`, `moduleResolution: bundler`). `turbo.json` `globalEnv` includes `VITE_API_BASE_URL`. The app UI is German-language by design.

## Conventions that bite

- **Package manager is `uv`** (Python) and **`pnpm`** (frontend) — never run `pip`, `poetry`, `npm`, or `yarn`.
- **Sharing rule**: only domain-neutral, transport-independent, non-business code goes in `packages/`. Profile, company, job, transfer, contract, application, and matching models stay inside the owning service. There is no shared database and no cross-service repository abstraction. `worker-contracts` holds versioned boundary DTOs, never a shared domain model.
- **No secrets, tokens, CVs, contracts, or raw source code in the repo or in logs** (CONTRIBUTING.md, product-scope.md).
- **Python 3.14 required** (`.python-version`); ruff `line-length=100`, `target-version=py314`, selects `E F I B UP ASYNC RUF`.
- New cross-cutting architectural decisions get an ADR in `docs/adr/`.
- Tests use `asyncio_mode = "auto"` (pytest-asyncio); no `@pytest.mark.asyncio` needed.

## Key docs

- `docs/architecture.md` — service shape, sharing rule, delivery sequence.
- `docs/product-scope.md` — consent, AI, data-acquisition, and security-by-design constraints. Read before touching anything consent- or AI-related.
- `docs/frontend.md` — frontend architecture detail.
- `docs/adr/` — architecture decision records.
- `AGENTS.md` — concise command + convention reference (mirrors much of the above).
