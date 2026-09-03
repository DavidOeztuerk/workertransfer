# AGENTS.md

The short reference. `CLAUDE.md` carries the reasons; this file carries the commands and the rules in one line each. When the two disagree, `CLAUDE.md` is right.

## What this is

WorkerTransfer is a consent-first talent-mobility platform, written in **.NET 10 on Girder 4.1.0** (a shared foundation library from GitHub Packages) with a React frontend.

It was a Python (`uv`) monorepo until August 2026 and was translated by hand. **No Python remains** — no `uv`, `ruff`, `mypy`, `pytest`, `alembic`, no `apps/<service>`, no `packages/worker-*`. A document that names those is describing the predecessor. `docs/MIGRATION-STAND.md` says what changed and what was measured.

The **ADRs in [`docs/adr/`](docs/adr/) still govern** — they hold reasons, and reasons do not change language. Terms in [`docs/glossary.md`](docs/glossary.md); constraints in [`docs/product-scope.md`](docs/product-scope.md).

## Repository structure

```text
src/
  identity-service/   accounts, sessions, companies, erasure — the reference shape
  consent-service/    the ledger everything else leans on
  profile-service/  resume-service/  portfolio-service/  jobs-service/
  applications-service/  companies-service/  transfer-service/
  notification-service/  github-service/
  gateway/            Ocelot — the single entrance
  shared/             ServiceDefaults, Outbox, Skills, Contracts.{Identity,Consent,Erasure}
tests/                fifteen suites, one per service plus Gateway, Outbox, Skills, Ganzes
web/                  the React app — deliberately not under src/, and the WHOLE frontend
deploy/  docker/  scripts/  docs/  bugs/
```

Eleven services plus the gateway. Ports 8001–8011, gateway on 8090.

## The order of checks (binding; `make check` runs it)

```bash
dotnet build WorkerTransfer.slnx   # warnings are errors
./scripts/test-dotnet.sh           # the suites, ONE AT A TIME
cd web && pnpm check               # tsc --noEmit
cd web && pnpm test                # Vitest
cd web && pnpm build               # the bundle
cd web && pnpm e2e                 # the Playwright journeys (needs `make up`)
```

**Build and test in separate invocations.** Chained, the Testcontainers suites fail and look like real test failures.

**Never `dotnet test` over the solution** — fifteen Postgres containers at once, the ResourceReaper times out, and every suite fails in a millisecond looking like a broken build.

## Commands

```bash
make env            # FIRST in a fresh clone: writes .env, rolls three secrets
make check          # the gate, fail-fast
make build / test   # .NET only, in that order
make check-web      # pnpm check + test + build
make validate       # runs through, reports every red step, prints the counts
make fix            # dotnet format
make up / down      # docker compose — the whole stack
make images         # both shipped images, the local twin of the CI job
make routenkarte    # every endpoint x three principals, needs the running stack
make k8s-up / k8s-down / k8s-lint
```

Single suite: `dotnet test tests/WorkerTransfer.<X>.Tests/WorkerTransfer.<X>.Tests.csproj --no-build`
Single frontend test: `cd web && pnpm exec vitest run src/app.test.tsx`. There is no workspace and no root `package.json` any more — `pnpm -r` and `--filter` fail with `ERR_PNPM_NO_PKG_MANIFEST`.

Restore needs a NuGet login for `Girder.*` (GitHub Packages); `NuGet.Config` pins source mapping so only Girder may come from there.

## Toolchain

- **Package managers**: `dotnet`/NuGet and `pnpm`. Never `npm`, never `yarn`.
- **Warnings are errors** (`Directory.Build.props`); package versions live in `Directory.Packages.props`, never in a `.csproj`.
- **Node 25** — the same major as `docker/web.Dockerfile` and both CI Node jobs. A drift there is how a `node:25` bump passed CI and broke the web image.
- **Tests**: xUnit + FluentAssertions + Testcontainers Postgres, one container per suite. They self-skip without Docker — and a skipped integration test looks exactly like a passing one, which is why the runner prints counts.

## Architecture in six lines

- One project per layer per service: `Domain` ← `Application` ← `Api`, with `Infrastructure` pointing inward; `Contracts` at the boundary.
- Repository interfaces in Domain, implementations in Infrastructure. All wiring behind one `Add<Service>Infrastructure()` (ADR-0003).
- `ServiceDefaults.AddWorkerTransferDefaults()` decides only what must not differ between services; everything that is a decision stays in the service.
- CQRS through Girder's `AddCQRS`, but with **our own `IBefehl`/`IAbfrage`** over MediatR's `IRequest` — Girder's `ICommand<T>` carries a second error envelope beside RFC 9457. `TransaktionsBehavior` wraps commands only.
- EF Core, one context and one database per service; **no shared database** (ADR-0004). Migrations run at startup via `ServiceDefaults.Wanderung`, retrying **only transient** failures.
- RFC 9457 everywhere, `correlationId` on every response. An endpoint filter that already wrote a response returns `Results.Empty`, never `null`.

## Key conventions

- **No secrets, tokens, CVs, contracts or raw source in the repo or in logs.** Girder logs no values either — do not build that back.
- **Sharing rule**: only domain-neutral, transport-independent code goes in `src/shared/`. Business models stay in the owning service. `Contracts.*` is DTOs, never a shared domain model.
- **A tenant is a company; a natural person has none** (ADR-0017). Modelled as `Capacity`: `AsSelf` or `ForCompany`. The consent ledger has no tenant column by design.
- **The token carries no tenant unless acting for a company**, and never carries roles — those are read from the membership table per operation (ADR-0018). `tenant_id` and `type` are gone; `TokenformTests` pins their absence.
- **Consent is read synchronously and never cached** (ADR-0013). `/check-batch` changes nothing about that (ADR-0030) — same read, one round trip, answers in the order of the questions.
- **Visibility lives only in the ledger** (ADR-0020). `profile-service` has no visibility field; 404 = hidden *or* absent (byte-identical), 403 = no active company, 503 = the ledger is silent.
- **The request is not the permission**: a resume request stays `granted` after a withdrawal and the read still comes up empty. That is the design.
- **Erasure deletes everything by default** (ADR-0027), hired applications and paid transfers included. The exception is one `static readonly` constant per service, `false`, and a constant rather than a setting. Delivery may fail, has no attempt ceiling, and a dead recipient blocks completion — that is the point.
- **No number that summarises a person** (ADR-0022) — and read the ADR before concluding more than that: it forbids exactly three things (a summarising number and rankings from it; derived attributes without basis; silent completeness) and explicitly permits evidence with provenance, consent first, visibility through the ledger. Fit is compared in the browser (`apps/web/src/jobs/match.ts`) and stored nowhere.
- **AI drafts on request and stores nothing** (ADR-0024). Two consumers, mirror images: a person's own text, a company's own advert. Each owns its own `IEntwerfer` and context type — no shared prompt with an `if`. No memory, no vector store, no background suggestion, no reflect loop, no ledger entry.
- **`WorkerTransfer.Skills` renames, never infers** (ADR-0023): aliases only. No levels, weights, implications or likelihood-to-switch. Unknown skills pass through unchanged.
- **Aggregates come back detached.** A mutation reaches the database only via an explicit save — forgetting it costs nothing in tests and loses the write in production.
- New cross-cutting decisions get an ADR in `docs/adr/`.

## Configuration

`make env` first, in a fresh clone. `.env.example` is complete with a comment per key; the three secrets ship **empty** and `make env` rolls them. `.env` is ignored.

- **No default for a secret.** `${X:?…}` in compose, never `${X:-wert}` — a built-in default *is* the secret and it lives in git. Without a value compose aborts and names the variable.
- **`Umgebung.Laden()` is the first line of all twelve `Program.cs`**, before `CreateBuilder`: the configuration builder reads the environment once, when it builds.
- **A set variable wins.** In compose and in the cluster the environment comes from there; a file in the image must never override it.
- Precedence: set environment > `.env` > `appsettings.json`. Girder does the same for `JWT_SECRET` over `JwtSettings:Secret`.

## The route map

`docs/routenkarte.yml` — every endpoint through the gateway with its expected answer in the three principals of ADR-0017 (no token / person without company / acting for a company).

- `RoutenkarteTests` (gateway suite, no stack) pins that the map is **complete**: a new route without an entry goes red.
- `scripts/routenkarte.sh` (`make routenkarte`, needs `make up`) drives the answers. A script and not a suite: a suite without a stack skips itself, and a skipped test looks like a passing one.
- **A deviation is not a reason to edit the map.** The map holds the intent; check which side is wrong first.

## Gateway

- Ocelot 25; `ocelot.json` carries its reasoning in `//` comments (the JSON config provider skips them).
- **`Sec-Fetch-Dest: document` routes to the SPA**, as *middleware* — in Ocelot a literal path beats a placeholder regardless of `Priority`, so a catch-all could never win against `/jobs`. Behind one origin `/jobs` is both an API prefix and a page; clicking works, only deep links and reloads broke, with raw JSON. Do not replace it with a path list.
- Health probes are middleware too — Ocelot terminates the pipeline, so a `MapGet` behind it never runs.
- Route order is pinned by `ReihenfolgeTests` against a **reversed** fixture; a probe that only equalised priorities stayed green by accident of file order.

## Docker and Kubernetes

- **One image for all twelve entry points** (ADR-0028) — `SERVICE_DIR` comes from the environment, not a build arg. The entrypoint finds the single `*.runtimeconfig.json` and **`cd`s into that directory**: ASP.NET's content root is the working directory.
- **`curl` is in the runtime image** so the container can answer its own healthcheck. The probe used to be `dotnet --version`, which can never work without an SDK (exit 155) — every service read `unhealthy` while serving fine.
- **One probe in the `x-dienst` anchor** for all twelve, asking `/health/live`, port derived from `ASPNETCORE_URLS` so no second list can drift.
- **Routes and initdb SQL come from the compose files** via `--set-file`, never copied. Both are `required`.
- **Probes must set `timeoutSeconds`** — the default is 1 s and it once restarted three healthy services.
- **The web image is a built artifact**, so URLs come from `window.__WT_CONFIG__` at runtime, not from `VITE_*` at build time.
- **`replicaCount` stays 1** for the eleven services, and for one reason now: every service migrates its schema at startup, so two pods race on one schema. (The dispatcher's missing `SKIP LOCKED` is fixed.) The **gateway** is pinned separately — the auth brake counts in-process.
- **`make k8s-up` has never been run.** The chart lints and renders; only a run proves it.

## The auth brake

Five paths, per origin, per minute, configured in `ocelot.json` beside the routes: `/auth/login` 20, `/auth/register` 5, `/auth/resend-verification` 3, `/auth/verify-email` 20, `/auth/refresh` 60. Implementation in `src/gateway/WorkerTransfer.Gateway/Bremse.cs`.

- **Per origin, never per email address.** A per-address limit would confirm the address exists — the enumeration channel `/auth/register` closes — and let a stranger lock a person out. The key is path plus origin; the body is never read.
- **In the gateway, because only there is the origin visible.** Behind it every service sees the gateway's address, so a brake in identity-service would put all people in one bucket and let the first mistyped password lock out everyone.
- **`X-Forwarded-For` is deliberately not read.** The caller sets it, so trusting it hands the attacker the counter's key. A test pins that a forged one changes nothing.
- **After the health probes, before authentication.** A braked liveness probe would be the outage; a brake behind bcrypt would cost a hash per attempt.
- **The counter is in-process** → the gateway stays at one replica. The way out is a registration change to `RedisDistributedRateLimitStore`, same interface.
- **Girder's input sanitization middleware runs, since 4.1.0 — but not over JSON bodies.** Until 4.0.2 it matched a *bare* SQL keyword on a word boundary (a hyphen is one), so `/jobs?q=Union-Investment` answered **400**, and because every request from the deletion page carried `Referer: …/delete-account` that whole page was dead down to its `/auth/session`. Fixed: it matches injection *syntax*, the `Referer` is not input, and JSON string values are inspected. We set `InspectJsonBodies = false` anyway (reason in `Dienstgrundlage.cs`): the filter runs first and cannot name a field, so a 422 saying *which* field is wrong becomes a blunt 400 — measured on five cases across three services. They are still refused; the person filling in the form is just told less. Query string and the two address headers stay inspected.
- **Girder's rate limiter is sound since 4.0.0, and is still left out of the eleven services — for topology, not distrust.** Measured without foreign code: `ClientAddress.Of` reads only `Connection.RemoteIpAddress`, a forged `X-Forwarded-For: 127.0.0.1` no longer lifts the brake, it counts per origin, does per-path limits and sets `X-RateLimit-*` and `Retry-After`. But **a service behind the gateway sees only the gateway as the origin**, so every caller would share one bucket. Braking happens at the entrance. `Bremse.cs` stays there for a reason of *kind*, not of defect (its 429 has been a problem document with a correlation id since 4.1.0): Girder's middleware is a **global** brake with per-path refinement, and the whole UI travels through this gateway. Ours brakes five named paths and touches nothing else.

## Branches

`feature → develop → main`. A hotfix is the single exception and its branch **must** be named `hotfix/…` — `backmerge.yml` triggers on that prefix only, so a `fix/…` branch silently never returns to develop.

## Planned, not built

[`docs/SCOUT-UND-BERATER.md`](docs/SCOUT-UND-BERATER.md) sketches `scout-service`, `advisor-service` and `assessment-service` with their conditions, interface rules and four Playwright journeys. It is intent, like [`docs/vision/`](docs/vision/). **Each needs its own ADR first. No agent builds any of it.**
