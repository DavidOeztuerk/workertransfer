# WorkerTransfer

WorkerTransfer is a consent-first talent platform for applications, direct recruiting, employment transfers, and AI-assisted career workflows.

It is **.NET 10 on [Girder](https://github.com/DavidOeztuerk) 3.0.1** — a shared foundation library carrying settings, logging, correlation, CQRS, health probes and password hashing — plus a React frontend. Eleven services and a gateway, each with its own database, its own migrations and its own composition root.

The repository was a Python (`uv`) monorepo until August 2026 and was translated by hand, service by service. The architecture decision records in [`docs/adr/`](docs/adr/) predate that and still govern: they hold the reasons, and reasons do not change language. [`docs/MIGRATION-STAND.md`](docs/MIGRATION-STAND.md) records what was decided and, more usefully, what was measured.

## What is in place

| service | port | |
|---|---|---|
| `identity-service` | 8001 | accounts, sessions, companies, memberships, invitations, account erasure |
| `consent-service` | 8002 | the consent ledger — the enabler everything else leans on |
| `profile-service` | 8003 | one profile per person |
| `resume-service` | 8004 | CVs, and the requests for them |
| `portfolio-service` | 8005 | work samples and their attachments |
| `jobs-service` | 8006 | job adverts |
| `applications-service` | 8007 | applications |
| `companies-service` | 8008 | employer profiles |
| `transfer-service` | 8009 | market status, market requests, transfers |
| `notification-service` | 8010 | notification preferences and an inbox |
| `github-service` | 8011 | a person's own, verified GitHub connection |
| gateway | 8090 | the single entrance |

Every service exposes `GET /health/live` and `GET /health/ready`.

## Start locally

The whole stack — Postgres, Mailpit, every service, the gateway and the web app — comes up with one command. Each container migrates its own schema on start, so a fresh clone needs nothing else:

```bash
docker compose up --build     # or: make up
docker compose down           # add -v to drop the databases
```

| | |
|---|---|
| Gateway (the entrance) | http://localhost:8090 |
| Web app (Vite dev server) | http://localhost:5173 |
| Mailpit — the confirmation link lands here | http://localhost:8025 |

Building needs a NuGet login: Girder lives in GitHub Packages, and `docker-compose.yml` passes `~/.nuget/NuGet/NuGet.Config` in as a BuildKit secret, so it never becomes a layer. If you do not have one yet:

```bash
dotnet nuget add source https://nuget.pkg.github.com/DavidOeztuerk/index.json \
  --name GitHub --username <you> --password <token> --store-password-in-clear-text
```

Source is **not** bind-mounted: a code change needs `docker compose up -d --build <service>`.

### Or as a staging environment, on kind

`docker compose` is the fast way to develop. To see what actually ships — built frontend artifact, no reload, code from the image — there is a Helm chart that runs against a local `kind` cluster, with no cloud account and no domain (`brew install kind helm` first):

```bash
make k8s-up      # cluster + images + release, then proves it: pods ready,
                 # routing through the gateway, and a registration whose
                 # confirmation mail lands in Mailpit
make k8s-down    # delete the cluster and its data
```

The app answers on **http://localhost:8090** — deliberately not 8080, which belongs to the compose gateway. Never run both at once. Staging and production differ from this by a different `values.yaml`, not a different structure (ADR-0028).

## Build and test

```bash
make check    # the gate: build → test → frontend, fail-fast
make build    # dotnet build. Warnings are errors
make test     # the suites, one at a time
make fix      # dotnet format
make validate # runs through instead of stopping, and prints how many tests ran
```

Two rules that save an hour each:

- **Build and test in separate invocations.** Chained, the Testcontainers suites fail *and look like real test failures*.
- **Never `dotnet test` over the solution.** Fifteen suites start fifteen Postgres containers at once; the ResourceReaper times out and every suite fails within a millisecond, looking exactly like a broken build.

The frontend is `pnpm` in `web/`, which carries its own lockfile — the pnpm
workspace, turbo and the root `package.json` are gone:

```bash
cd web
pnpm install
pnpm check    # tsc --noEmit
pnpm test     # Vitest
pnpm build
pnpm dev
```

Playwright journeys in `web/e2e/` run against the **real** compose stack; without it they skip themselves. `make validate-e2e` runs them and names the skips.

## Architecture and product guardrails

The technical design and delivery sequence live in [docs/architecture.md](docs/architecture.md). Product, consent, AI and integration guardrails live in [docs/product-scope.md](docs/product-scope.md) — read that one before touching anything consent- or AI-related. [CLAUDE.md](CLAUDE.md) collects the rules that carry the design together with the reason for each.

## Current boundary

This is a working system, not a pretend-complete recruiting product. Some things are deliberately absent:

- There is **no S3 backend** for stored files, no orphan collection, and no upload UI (ADR-0021).
- The consent ledger has **no admin surface**; a person manages their own releases.
- `make k8s-up` has **never been run** — the chart lints and renders, but only a run proves it.
- The **auth brake** is back (gateway, per origin, five paths) but its counter is in-process, which pins the gateway to one replica.

[`docs/SCOUT-UND-BERATER.md`](docs/SCOUT-UND-BERATER.md) and [`docs/vision/`](docs/vision/) describe what is intended next. They are intent, not description, and each new service needs its own ADR first.
