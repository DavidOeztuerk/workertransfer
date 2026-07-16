# WorkerTransfer — local developer tasks.
#
# The binding 4-step gate order (ruff format → ruff check → mypy → pytest) is
# encoded in `make check`. CI (`.github/workflows/ci.yml`) runs the same steps in
# the same order. Contributors run `make check` before merging; `make fix`
# autoremits format/import issues.
#
# Python lifecycle goes through `uv` (never pip/poetry); frontend through `pnpm`.

.PHONY: help check lint fix type test sync ci clean

help:  # Show this help (default target).
	@awk 'BEGIN {FS = ":.*#"} /^[a-zA-Z_-]+:.*# / {printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

check:  # Definition-of-Done gate: the 4-step order, fail-fast.
	uv run ruff format --check .
	uv run ruff check .
	uv run mypy packages apps
	uv run pytest

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

sync:  # Install every workspace package + dev group.
	uv sync --all-packages --all-groups

ci: check  # Mirror the CI job locally (same steps, same order).

clean:  # Remove caches + bytecode artifacts.
	find . -type d -name __pycache__ -prune -exec rm -rf {} +
	rm -rf .mypy_cache .pytest_cache .ruff_cache
