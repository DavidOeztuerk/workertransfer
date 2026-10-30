---
name: wt-stack
description: Use when starting, configuring or debugging WorkerTransfer's local docker compose stack — `make env`, `.env` and its keys (including an old `.env` with German names), required secrets in compose, `EnvironmentFile.Load()`, schema migration at startup, the one image for all entry points, `/health/ready`, the data-protection key ring, Valkey token revocation, or the container healthcheck.
---

# The local stack

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liners stay there.

### Configuration comes from the environment

`make env` is the first command in a fresh clone. It copies `.env.example` — which is complete, one comment per key — and **rolls four secrets** with `openssl rand -base64 32` and generates the token key pair. `.env` is git-ignored; `.env.example` ships with the secrets **empty**.

That emptiness is the point. A built-in default *is* the secret, and it then lives in git. `docker-compose.yml` therefore uses `${WORKERTRANSFER_JWT_SECRET:?…}`, not `${…:-dev-only-secret}`: without a value, compose aborts and **names the missing variable**. Noelia does the same at its own most important place — `JWT_SECRET` beats `JwtSettings:Secret`, and if both are absent it throws a `ConfigurationException` naming the key.

`EnvironmentFile.Load()` (`ServiceDefaults`) is the **first line of every one of the fifteen `Program.cs`**, before `CreateBuilder` — the configuration builder reads environment variables exactly once, when it builds, so loading afterwards means loading and nobody reading. A test pins that order in all fifteen. It never overwrites an already-set variable: in compose and in the cluster the environment comes from there, and a file left in the image must never override it.

**An `.env` written before 25.09.2026 carries German variable names** — the keys were renamed in one pass (`229de78`, whose body lists the old and new names), and nobody renames a git-ignored file for you. Compose aborts on the one required variable that moved (`WORKERTRANSFER_EVIDENCE_SECRET`); the optional ones fall back **silently** — `WORKERTRANSFER_BRAKE_FACTOR` to 1, the AI targets and the GitHub redirect to their defaults. Rename them by hand; rolling a fresh `.env` also rolls `WORKERTRANSFER_SECRETS_KEY`, and whatever is encrypted under the old one stops opening.

Infisical will later fill the environment. It feeds `.env`; it does not replace the mechanism, so no code changes for it.

### The stack

`docker compose up` is the whole local environment; there is no companion script. Each service migrates its own schema on start (`ServiceDefaults.SchemaMigration`), so a fresh clone needs no manual step. Source is not bind-mounted — a code change needs `docker compose up -d --build <service>`.

**One image serves all sixteen entry points** — fifteen services and the gateway (ADR-0028). They differ only in `SERVICE_DIR`, which `docker/dotnet-entrypoint.sh` reads from the *environment*, not from a build arg — so the image is built once, without the arg, and the container says who it is. The entrypoint finds the entry assembly as the only `*.runtimeconfig.json` under `/app/$SERVICE_DIR` (no name table to keep in sync) and **`cd`s into that directory** before starting: ASP.NET's content root is the working directory, and from `/app` no service reads its own `appsettings.json`. Measured at the first `compose up`, where only the gateway died visibly, on its missing `ocelot.json`.

Adding a service is three steps and no new Dockerfile: its database in `scripts/initdb/`, its entry point in `docker/dotnet-service.Dockerfile`, a copied block in `docker-compose.yml` with three values changed — plus its route in `src/gateway/WorkerTransfer.Gateway/ocelot.json`.

**`/health/ready` answers for the database, and until 21.09.2026 it answered for nothing.** The endpoint filters the registered checks by the `ready` tag; with none registered the filtered set is empty, an empty report counts as healthy, and the service reports ready over an unreachable database — the orchestrator then routes traffic to a service that answers every request with 500. `Readiness.AddDatabaseReadiness<T>(serviceName)` is called in all fourteen composition roots, one line next to the `AddDbContext` it belongs to. It asks `CanConnectAsync` — not a query, because a readiness probe runs on a timer and one that generates load finishes off a struggling service. **Its description names no value**: `/health/ready` answers anyone, and an Npgsql exception carries host, port and sometimes the user; the exception travels as the second field and reaches the log, not the response. `ReadinessJourneyTests` reads the *body*, not the status — the status is green precisely when nothing is checked.

**ASP.NET's own key ring lives in Postgres, encrypted under the operator's master key** (`KeyRing`, ADR-0046). Left alone the framework writes it into the container's filesystem and says so twice at every start — and anything protected with it stops verifying when the container is replaced. Nothing in this tree reads it *today*; it is configured anyway, because the framework registers it whether or not you want it, and a gate that stays red gets ignored. **Not `Noelia.InMemory`**: that would satisfy the check while two instances still read different rings. The table carries no `DbSet` — it belongs to no domain model, and in the EF model it would look like a `SubjectKeyedTable` to the erasure guard.

**A sign-out reaches all fourteen services, through Valkey — since 07.10.2026.** identity-service ended a session, but the other thirteen verify the access token by signature alone and honoured it for up to fifteen minutes after a sign-out or an account erasure. Now identity writes the revocation into **Valkey** (`ConnectionStrings__valkey`; BSD-licensed, protocol-equal to Redis, which left open source in 2024): the token's `jti` on sign-out, everything before "now" on erasure. Three things are easy to undo:

- **`app.UseTokenRevocation()` stands in `UseWorkerTransferDefaults` and has to.** Noelia's default chain does not check revocations even with a store registered, while `noelia.revocation.degradation` reports green on mere registration. Measured: without the line both revocation tests pass the write and the token still opens everything.
- **Only the connection is registered, never `AddRedisConnection`**, which also installs a distributed cache as a side effect (`Revocation.cs`).
- **Without the key, Staging and Production refuse to start**; elsewhere — the test hosts — revocations stay in the process. Test tokens therefore carry `jti` and `iat` like real ones: the check refuses a token without them.

Revoking a single *session* did not work in Noelia 7.0.0 (`bugs/sitzungswiderruf-liest-sid.md`); fixed in 7.0.1, which this tree uses. Nothing here revokes single sessions yet.

**`curl` is in the runtime image on purpose.** Docker's healthcheck asks from *inside* the container, and the aspnet runtime image ships no curl, no wget, no nc; `sh` is dash and cannot do `/dev/tcp`. The probe used to be `dotnet --version`, which can *never* succeed on a runtime image (no SDK, exit 155) — every service reported `unhealthy` for months while answering perfectly, and a probe that is always red is worse than none. One probe now lives in the `x-service` anchor, covers all fourteen services *and* the gateway, asks `/health/live`, and derives the port from `ASPNETCORE_URLS` so no second list of ports can drift. Kubernetes does not need any of this — there the kubelet asks over HTTP from outside.
