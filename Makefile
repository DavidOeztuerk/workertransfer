# WorkerTransfer — local developer tasks.
#
# The binding gate order (AGENTS.md) is: ruff format → ruff check → mypy →
# pytest → pnpm check → pnpm test. `make check` runs all six fail-fast;
# `make check-py` / `make check-web` run one ecosystem each. CI
# (`.github/workflows/ci.yml`) runs the same steps in the same order, split
# across a backend and a frontend job. `make fix` autoremits format/import
# issues.
#
# Python lifecycle goes through `uv` (never pip/poetry); frontend through `pnpm`.

.PHONY: help check check-py check-web validate validate-e2e lint fix type test test-web sync ci clean dev

help:  # Show this help (default target).
	@awk 'BEGIN {FS = ":.*#"} /^[a-zA-Z_-]+:.*# / {printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

check: check-py check-web  # Definition-of-Done gate: all six steps, fail-fast.

check-py:  # Python gate: format-check → lint → types → tests.
	uv run ruff format --check .
	uv run ruff check .
	uv run mypy packages apps
	uv run pytest

validate:  # Wie check, aber läuft durch und berichtet den Stand statt beim ersten Fehler zu enden.
	./scripts/validate.sh

validate-e2e:  # Zusätzlich die Browser-Reise; braucht den laufenden Stack.
	./scripts/validate.sh --e2e

check-web:  # Frontend gate: TypeScript + Vitest across the pnpm workspace.
	pnpm check
	pnpm test

lint:  # Static gates only (no tests): format-check + lint.
	uv run ruff format --check .
	uv run ruff check .

fix:  # Autoremit format + import issues (ruff format + ruff check --fix).
	uv run ruff format .
	uv run ruff check . --fix

type:  # Type-check only.
	uv run mypy packages apps

test:  # Run pytest only.
	uv run pytest

test-web:  # Run the frontend test suite only.
	pnpm test

sync:  # Install every workspace package + dev group.
	uv sync --all-packages --all-groups

ci: check  # Mirror the CI job locally (same steps, same order).

dev:  # Start backend + frontend together locally.
	./scripts/run-dev.sh

clean:  # Remove caches + bytecode artifacts.
	find . -type d -name __pycache__ -prune -exec rm -rf {} +
	rm -rf .mypy_cache .pytest_cache .ruff_cache
