# WorkerTransfer

WorkerTransfer is a consent-first talent platform for applications, direct recruiting,
employment transfers, and AI-assisted career workflows.

The repository starts with the platform foundation rather than a collection of copied
microservice boilerplates. The first working service is `identity-service`; it proves
the conventions that every future service will use.

## What is in place

- Python 3.14 `uv` workspace with a single, reproducible lockfile
- `worker-core` for framework-independent domain building blocks
- `worker-platform` for HTTP composition, settings, structured logs, correlation IDs,
  secure tenant-context plumbing, RFC 9457-style problems, health probes, and CQRS
- `identity-service` as the reference service, with liveness and readiness endpoints
- automated quality checks and GitHub Actions CI

## Start locally

```bash
uv sync --all-packages --all-groups
uv run worker-identity
```

The reference service then exposes:

- `GET /health/live`
- `GET /health/ready`

Run the checks with (binding order, same as CI):
```bash
make check   # ruff format-check → ruff check → mypy → pytest
make fix     # ruff format + ruff check --fix
```
Equivalent explicit steps:
```bash
uv run ruff format --check .
uv run ruff check .
uv run mypy packages apps
uv run pytest
```

The React frontend workspace is initialized separately:

```bash
pnpm install
pnpm check
pnpm test
pnpm dev
```

## Architecture and product guardrails

The technical design and delivery sequence live in
[docs/architecture.md](docs/architecture.md). Product, consent, AI, and integration
guardrails live in [docs/product-scope.md](docs/product-scope.md).

## Current boundary

This is intentionally the foundation, not a pretend-complete recruiting product.
PostgreSQL, Redis, messaging, OAuth, file storage, search, worker processes, the
gateway, the frontend, and the actual domain use cases are added as tested vertical
slices after this baseline is stable.
