# Skill: Worker CLI (`worker new-service` and friends)

## Purpose
Scaffold services, packages, and Clean-Architecture components from the
`worker-cli` template tree, instead of hand-copying boilerplate. Keeps every
new service consistent with the kernel/library split (ADR-0002) and the
Composition-Root convention (ADR-0003).

## When to use
- Starting any new phase that introduces a new `apps/<service>`.
- Adding a domain or CQRS piece to an existing service.
- When you find yourself reaching for copy-paste between services.

## Prerequisites
- The CLI is installed and runnable: `uv run worker --help` must succeed.
  (Phase 1.3 repairs the `worker_cli.main` script path; until then the CLI is
  broken — do not rely on it.)
- Templates live under `packages/worker-cli/src/worker_cli/templates/`.

## Commands
```bash
worker new-service <name>            # apps/<name> via Clean-Architecture scaffold
worker new-package  <name>          # packages/<name> shared library
worker command  <Name> --service <s> # command + handler scaffold
worker query    <Name> --service <s> # query + handler scaffold
worker entity      <Name> --service <s> --fields a:int,b:str
worker aggregate   <Name> --service <s>
worker valueobject <Name> --service <s> --fields ...
worker event       <Name> --service <s> --type domain|integration|application
worker consumer    <Name> --service <s> --event <Event>
worker publisher   <Name> --service <s> --event <Event>
worker migrate  "msg" --service <s>   # uv run alembic revision --autogenerate
worker upgrade                  --service <s>   # uv run alembic upgrade head
```

## Conventions
- The generated service drains from `worker-platform` for the HTTP shell and
  selects `worker-*` libraries in a `compose.py` Composition-Root (ADR-0003).
  If the template still emits a fluent builder or no `compose.py`, update the
  template as part of the work.
- Domain code lives under `src/<service>/domain/...`, never in `worker-core`
  beyond the primitive bases (`Entity`, `ValueObject`, …). Business entities
  stay in the owning service (sharing rule, ADR-0001).
- Run the four checks after every generation:
  `uv run ruff format --check . && uv run ruff check . && uv run mypy packages apps && uv run pytest`.

## Anti-patterns
- Using the CLI as an excuse to skip the Composition-Root review — the scaffold
  is a starting point, not a finished service.
- Letting generated code drift from ADR-0002 (kernel vs libraries) without a
  template fix.
