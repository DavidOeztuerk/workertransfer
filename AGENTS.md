# AGENTS.md

## Masterplan & Roadmap

Der stufenweise Masterplan liegt in [`docs/ULTRAPLAN.md`](docs/ULTRAPLAN.md)
(inkl. Status der heutigen Foundation, Leitarchitektur, zehn Phasen je mit
Definition of Done, Risiko-Register). Der Pull-Through-Index der Phasen ist
[`docs/ROADMAP.md`](docs/ROADMAP.md). Architekturentscheidungen in
[`docs/adr/`](docs/adr/) (ADR-0002 Kernel-vs-Bausteine, ADR-0003
Composition-Root statt fluent Builder, ADR-0004 Verträge/kein
Scraping/Consent-First). Begriffe in [`docs/glossary.md`](docs/glossary.md).
Domain-Skills unter [`docs/skills/`](docs/skills/) (worker-cli, consent-ledger,
transfer-market …) — ergänzend zu den opencode-spezifischen Skills unter
`.opencode/skill/`.

**Gültige Reihenfolge der Checks** (bindend, gleicht CI; abgedeckt durch `make check`):
1. `uv run ruff format --check .`
2. `uv run ruff check .`
3. `uv run mypy packages apps`
4. `uv run pytest`
5. `pnpm check`
6. `pnpm test`

## Repository structure

This is a **dual-ecosystem monorepo**: Python (uv workspace) + frontend (pnpm + turbo).

```text
apps/
  identity-service/   auth vertical slice — the reference service shape
  consent-service/    consent ledger (Phase 3, ~10% built)
  web/                React frontend (pnpm workspace member)
packages/             34 Python packages + 1 TypeScript package
  worker-core/        framework-free domain primitives
  worker-platform/    the kernel: settings, context, logging, CQRS, middleware, health, errors
  worker-shared/      domain-neutral primitives (utc_now, Page, Cursor, Money)
  worker-*/           composable infrastructure libraries (auth, database, events, …)
  ui/                 shared React components (Button, Card)
tests/                repo-level architectural guards
```

Most `worker-*` packages have one smoke test and no production consumer. `worker-ai`
is **excluded from the uv workspace** (`worker-files` was deleted, ADR-0021); `worker-github` is
unimportable (PyGithub vs. githubkit mismatch). Verify before depending on any of them.

## Python workspace

**Requires Python 3.14** (see `.python-version`).

```bash
uv sync --all-packages --all-groups
make check      # all six steps, fail-fast
make check-py   # Python only
make check-web  # frontend only
```
Equivalent explicit steps (the binding order):
```bash
uv run ruff format --check .
uv run ruff check .
uv run mypy packages apps
uv run pytest
```

- **Package manager**: uv (not pip/poetry)
- **Linter/formatter**: ruff (line-length=100, target py314)
- **Type checker**: mypy strict mode (excludes tests)
- **Test runner**: pytest with `asyncio_mode = "auto"`
- **Service entrypoints**: `apps/<service>/src/<module>/main.py`, exposed as `[project.scripts]`
- **Python 3.14 must be a *final* release**, not an rc — pydantic breaks on 3.14.0rc2

### Service architecture

Every service follows Clean Architecture layers:
```text
Presentation -> Application -> Domain
     |                           ^
     └------ Infrastructure ------┘
```

- `worker-core`: domain primitives, no framework dependencies
- `worker-platform`: FastAPI-based HTTP utilities, settings, logging, CQRS

## Frontend workspace

**Requires Node >=24, pnpm >=11** (see `package.json` engines).

```bash
pnpm install
pnpm check    # TypeScript type-checking
pnpm test     # Vitest
pnpm dev      # Vite dev server (turbo parallel)
```

- **Package manager**: pnpm (not npm/yarn)
- **Build orchestration**: turbo
- **Test framework**: Vitest
- **UI package**: `@workertransfer/ui` (workspace:*)

## Key conventions

- **No secrets in source control** (CONTRIBUTING.md)
- **Tests ignore**: mypy excludes `tests/` directories
- **Ruff ignores**: `S101` (assert) allowed in test files
- **Clean Architecture**: domain has no FastAPI, database, or transport dependencies
- **Sharing rule**: only domain-neutral code goes in `packages/`; business models stay in owning service
- **No number that summarises a person** (ADR-0022): no score, no percentage, no ranking of people. Job/profile fit is compared in the browser (`apps/web/src/jobs/match.ts`) and stored nowhere
- **AI drafts on request, stores nothing** (ADR-0024): `NullDrafter` is the default; `DraftContext` carries no name/email/subject_id; no prompt or answer is ever logged or persisted
- **`worker-skills` renames, never infers** (ADR-0023): aliases only (`postgres` → `PostgreSQL`). No implications, levels, weights, or likelihood-to-switch. Unknown skills pass through unchanged
- **Auth edge is throttled per origin, never per email address** (`worker_platform.presentation.throttle`): a per-address limit would both confirm the address exists and let a stranger lock a person out

## Verification order

1. `uv run ruff format --check .` (formatting)
2. `uv run ruff check .` (linting)
3. `uv run mypy packages apps` (type checking)
4. `uv run pytest` (tests)
5. `pnpm check` (frontend types)
6. `pnpm test` (frontend tests)
