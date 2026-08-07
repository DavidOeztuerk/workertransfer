# Contributing

## Prerequisites

- Python 3.14 (the repository uses `.python-version`)
- [uv](https://docs.astral.sh/uv/)

## Setup and checks

Run before pushing/merging (binding order, same as CI):
```bash
uv sync --all-packages --all-groups
make check   # ruff format-check → ruff check → mypy → pytest (fail-fast)
make fix     # autoremits ruff format + import-fix issues
```
Equivalent explicit steps:
```bash
uv run ruff format --check .
uv run ruff check .
uv run mypy packages apps
uv run pytest
```

Keep each change focused, test the affected behaviour, and update an ADR when a
cross-cutting architectural decision changes.

Do not add real secrets, personal documents, access tokens, candidate data, or
provider payloads to source control.

The staged masterplan and per-phase Definition of Done live in
[docs/ULTRAPLAN.md](docs/ULTRAPLAN.md); the phase status index is
[docs/ROADMAP.md](docs/ROADMAP.md). Read the relevant phase before starting
work on a package or service; a phase's Definition of Done must hold before
moving on. Architectural cross-cutting decisions are recorded as ADRs in
[docs/adr/](docs/adr/) — add one when a phase changes the architecture.
