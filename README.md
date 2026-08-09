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

The whole stack — Postgres, every backend service and the web app — comes up with
one command. Each service container runs its own migrations on start, so a fresh
clone needs nothing else:

```bash
docker compose up --build
docker compose down        # add -v to drop the databases
```

| | |
|---|---|
| Web app | http://localhost:5173 |
| identity-service | http://localhost:8001 |
| consent-service | http://localhost:8002 |

Source is bind-mounted and the services run under `--reload`, so edits apply
without a rebuild. Rebuild only when a dependency changes.

### Or as a staging environment, on kind

`docker compose` is the fast way to develop. To see what actually ships —
built frontend artifact, no reload, code from the image — there is a Helm chart
that runs against a local `kind` cluster, no cloud account and no domain needed
(`brew install kind helm` first):

```bash
make k8s-up      # cluster + images + release, then proves it: pods ready,
                 # GET /jobs, and a registration whose mail lands in Mailpit
make k8s-down    # delete the cluster and its data
```

The app answers on **http://localhost:8090** — deliberately not 8080, which
belongs to the compose gateway. Never run both at once. Mailpit is on
**http://localhost:8025**, where the registration confirmation link lands.
Staging and production differ from this by a different `values.yaml`, not a
different structure (ADR-0028).

To run a single service on the host instead:

```bash
uv sync --all-packages --all-groups
uv run worker-identity
```

Every service exposes:

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
