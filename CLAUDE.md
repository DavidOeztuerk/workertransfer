# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

WorkerTransfer is a consent-first talent-mobility platform (applications, direct recruiting, employment transfers, AI-assisted career workflows). It is **.NET 10 on [Girder](https://github.com/DavidOeztuerk) 4.4.0** — a shared foundation library of this author's, pulled from GitHub Packages — plus a React app.

Twelve services and a gateway live under `src/`, their tests under `tests/`, the React app in `web/`.

The repository was a Python (`uv`) monorepo until August 2026 and was translated by hand, service by service. **Everything Python is gone**: no `pyproject.toml`, no `uv.lock`, no `apps/<service>`, no `packages/worker-*`, no alembic, ruff, mypy or pytest. `docs/MIGRATION-STAND.md` records what was decided along the way and, more usefully, what was *measured* — read it before assuming a shape is arbitrary. The ADRs in [`docs/adr/`](docs/adr/) predate the migration and **still govern**: they hold the reasons, and reasons do not change language. Where an ADR names a Python path, read it as naming the decision, not the file.

### The layout, so nobody guesses

```
src/            twelve services, gateway/, shared/
tests/          seventeen test projects
web/            the React app — deliberately not under src/
docs/  bugs/  deploy/  docker/  scripts/  .github/
WorkerTransfer.slnx  Directory.Build.props  Directory.Packages.props  NuGet.Config
package.json  pnpm-lock.yaml  pnpm-workspace.yaml  turbo.json  tsconfig.base.json
```

`web/` is **not** under `src/` and is not moving: it is frontend rather than a leftover, and `src/` means .NET here. It is also the *whole* frontend now — `apps/`, `packages/ui`, the pnpm workspace, turbo and the root `package.json` are gone, and `web/` carries its own lockfile. Anything that still says `apps/web` or `packages/ui` is describing the predecessor.

### The services

| service | port | what it owns |
|---|---|---|
| `identity-service` | 8001 | accounts, sessions, companies, memberships, invitations, account erasure |
| `consent-service` | 8002 | the consent ledger — the enabler everything else leans on |
| `profile-service` | 8003 | one profile per person |
| `resume-service` | 8004 | CVs and the requests for them |
| `portfolio-service` | 8005 | work samples and their attachments |
| `jobs-service` | 8006 | job adverts |
| `applications-service` | 8007 | applications |
| `companies-service` | 8008 | employer profiles |
| `transfer-service` | 8009 | market status, market requests, transfers |
| `notification-service` | 8010 | notification preferences and an inbox |
| `github-service` | 8011 | a person's own, verified GitHub connection |
| `scout-service` | 8012 | the search for people — ticks and evidence, never a number |

The gateway (`src/gateway`, port 8090) is the single entrance. `src/shared/` holds seven things and no more: `ServiceDefaults` (the one call every service makes), `Outbox`, `Skills`, `Ablage` (bytes for certificates, never a PDF the server rendered), and three `Contracts.*` packages (`Identity`, `Consent`, `Erasure`) that carry versioned boundary DTOs — never a shared domain model.

The vision documents in [`docs/vision/`](docs/vision/) describe a much larger future state. **Treat them as intent, not description.**

## Commands

Everything has a `make` target, and the target is the twin of what CI runs. `make help` lists them.

```bash
make env            # FIRST, in a fresh clone: writes .env and rolls three secrets
make check          # the gate: build, then test, then the frontend — fail-fast
make build          # dotnet build. Warnings are errors (Directory.Build.props)
make test           # the suites, ONE AT A TIME (see below)
make check-web      # pnpm check + test + build
make fix            # dotnet format
make validate       # like check, but runs through and reports every red step
make up / make down # the whole stack in docker compose
make images         # both shipped images — the local twin of the CI job
make routenkarte    # every endpoint × four principals, against the running stack
make k8s-up / k8s-down / k8s-lint
```

**Build and test in separate invocations.** Chained (`build && test`), the Testcontainers suites fail *and look like real test failures*. This has cost time more than once.

**Never `dotnet test` over the solution.** Fifteen suites start fifteen Postgres containers at once, Testcontainers' ResourceReaper times out, and *all* suites fail within a millisecond with `TypeInitializationException` — which looks exactly like a broken build and is not one. `scripts/test-dotnet.sh` runs them one at a time, runs through instead of failing fast, prints how many tests actually ran, and goes red if a suite executed nothing. A green run that silently collected half the suite looks identical to a real one unless the count is on screen.

Single suite / single test:

```bash
dotnet test tests/WorkerTransfer.Identity.Tests/WorkerTransfer.Identity.Tests.csproj --no-build
dotnet test tests/WorkerTransfer.Consent.Tests/... --no-build --filter "FullyQualifiedName~Widerruf"
```

**Restoring needs no login any more.** Girder went to nuget.org on 10.09.2026 (MIT, source at [DavidOeztuerk/girder](https://github.com/DavidOeztuerk/girder)), so `dotnet restore`, `docker compose up` and CI all work in a fresh clone with no token. Measured against an empty package cache: every Girder assembly came back with `"source": "https://api.nuget.org/v3/index.json"`.

Before that it lived in GitHub Packages, and that cost a whole evening of CI — the notes are in the CI section, and the distinction worth keeping is **403 means authenticated and refused, 401 means never authenticated at all.**

`NuGet.Config` still pins package source mapping, and the reason has only shifted. It used to keep a foreign `Girder.*` on nuget.org away from us; now it pins that exactly **one** source may answer those names. Without it, any additionally configured source — a company mirror, a local folder — is asked for every name, and whoever answers first wins. `<clear />` stays for the same reason: it discards the sources a developer machine carries in its user-level config.

### Configuration comes from the environment

`make env` is the first command in a fresh clone. It copies `.env.example` — which is complete, one comment per key — and **rolls the three secrets** with `openssl rand -base64 32`. `.env` is git-ignored; `.env.example` ships with the secrets **empty**.

That emptiness is the point. A built-in default *is* the secret, and it then lives in git. `docker-compose.yml` therefore uses `${WORKERTRANSFER_JWT_SECRET:?…}`, not `${…:-dev-only-secret}`: without a value, compose aborts and **names the missing variable**. Girder does the same at its own most important place — `JWT_SECRET` beats `JwtSettings:Secret`, and if both are absent it throws a `ConfigurationException` naming the key.

`Umgebung.Laden()` (`ServiceDefaults`) is the **first line of every one of the thirteen `Program.cs`**, before `CreateBuilder` — the configuration builder reads environment variables exactly once, when it builds, so loading afterwards means loading and nobody reading. A test pins that order in all thirteen. It never overwrites an already-set variable: in compose and in the cluster the environment comes from there, and a file left in the image must never override it.

Infisical will later fill the environment. It feeds `.env`; it does not replace the mechanism, so no code changes for it.

### The stack

`docker compose up` is the whole local environment; there is no companion script. Each service migrates its own schema on start (`ServiceDefaults.Wanderung`), so a fresh clone needs no manual step. Source is not bind-mounted — a code change needs `docker compose up -d --build <service>`.

**One image serves all thirteen entry points** (ADR-0028). They differ only in `SERVICE_DIR`, which `docker/dotnet-entrypoint.sh` reads from the *environment*, not from a build arg — so the image is built once, without the arg, and the container says who it is. The entrypoint finds the entry assembly as the only `*.runtimeconfig.json` under `/app/$SERVICE_DIR` (no name table to keep in sync) and **`cd`s into that directory** before starting: ASP.NET's content root is the working directory, and from `/app` no service reads its own `appsettings.json`. Measured at the first `compose up`, where only the gateway died visibly, on its missing `ocelot.json`.

Adding a service is three steps and no new Dockerfile: its database in `scripts/initdb/`, its entry point in `docker/dotnet-service.Dockerfile`, a copied block in `docker-compose.yml` with three values changed — plus its route in `src/gateway/WorkerTransfer.Gateway/ocelot.json`.

**`curl` is in the runtime image on purpose.** Docker's healthcheck asks from *inside* the container, and the aspnet runtime image ships no curl, no wget, no nc; `sh` is dash and cannot do `/dev/tcp`. The probe used to be `dotnet --version`, which can *never* succeed on a runtime image (no SDK, exit 155) — every service reported `unhealthy` for months while answering perfectly, and a probe that is always red is worse than none. One probe now lives in the `x-dienst` anchor, covers all twelve services *and* the gateway, asks `/health/live`, and derives the port from `ASPNETCORE_URLS` so no second list of ports can drift. Kubernetes does not need any of this — there the kubelet asks over HTTP from outside.

### Kubernetes — a staging environment on your own machine

```bash
make k8s-up      # kind cluster + both images + helm release + PROOF
make k8s-down    # delete the cluster and its data
make k8s-lint    # helm lint + render, no cluster needed
```

The app answers on **`http://localhost:8090`** — not 8080, which belongs to the compose gateway; two environments fighting over one port produce exactly the debugging session where nobody can say who answered. Mailpit is on `:8025`.

Load-bearing, and easy to undo by tidying up (ADR-0028):

- **The route map stays one file.** The k8s `Service` objects are named exactly like the compose services, so `ocelot.json` travels in the image and the same map serves both. Re-expressing those non-disjoint paths and their priorities as Ingress rules would duplicate the map, and the copy would be wrong at the first new path.
- **`replicaCount: 1` is a decision, not a starting value.** For the twelve services one reason remains: **every service migrates its schema at startup**, so two pods are two pilgrims on one schema. (The outbox dispatcher's missing `SKIP LOCKED` is fixed.) The **gateway** is pinned separately, and for its own reason: the auth brake counts in-process — see below.
- **The web image is a built artifact, and that forces a runtime config.** `vite build` bakes `import.meta.env.VITE_*` into the bundle, so an image with baked URLs cannot be the same in two environments. `web/src/env.ts` resolves in three steps — `window.__WT_CONFIG__` → `VITE_*` → port fallback — and `web/public/config.js` is an empty object that changes nothing locally. Only the chart lays a real one over it. Do not "simplify" that back to a build arg.

**`make k8s-up` has never been run on this machine.** The chart lints and renders; only a run proves it works.

### The brake on the auth endpoints, and why it lives in the gateway

Five paths are braked — `/auth/login` (20/min), `/auth/register` (5), `/auth/resend-verification` (3), `/auth/verify-email` (20), `/auth/refresh` (60) — per origin, per minute. The rules live in `ocelot.json` next to the routes they select from, as Girder's own `DistributedRateLimiting` section; the middleware is **Girder's**, and there is no hand-written brake here any more.

**The three defaults are `0`, and that is the whole trick.** A limit of zero writes no counter, so only the five named paths count. Without it a default would apply to every path — and the whole UI travels through this gateway, so each asset fetch would count. `BremsenkarteTests` pins both: the five paths, and that the defaults stay at zero.

**`WORKERTRANSFER_BREMSE_FAKTOR` multiplies every limit, and it is why a measurement can look broken.** Measured 09.09.2026: six calls to `/auth/register` (limit 5) all came back `201`, and the answer carried `X-RateLimit-Limit: 200` — a number that appears nowhere in `ocelot.json`. It is `5 × 40`: `.env` sets the factor to 40 so an E2E run does not brake itself. **A factor and not a switch, deliberately** — the limiter still counts, still keys per origin, still answers with its own headers, so what is measured is the real path and not a disabled one. With the factor at `1` the promise holds exactly: three through, the fourth `429` with `X-RateLimit-Limit: 3`, `X-RateLimit-Remaining: 0`, `Retry-After: 60`, an RFC 9457 body, and the supplied correlation id in **both** header and body.

Two suites, because one could not have found this: `BremsenkarteTests` asserts each braked path has a route — a statement about the *selection*. `BremsenbindungTests` binds the real `ocelot.json` to Girder's options type and asserts what arrives — the *binding*. A configuration that never lands still has perfectly valid paths.

**Per origin, never per email address.** Keying on the address would build exactly the enumeration channel `/auth/register` closes: a braked answer would confirm the address exists. It would also let a stranger lock out anyone whose address they know. The key is path plus origin, and the brake never reads the body — a test sends five *different* addresses from one origin and requires them to share one bucket.

**In the gateway, and that was measured rather than chosen.** A brake needs the caller's origin, which lives in `Connection.RemoteIpAddress`. Behind the gateway that is the *gateway's* address, identical for everyone — a brake inside identity-service would have thrown all people into one bucket, so the first person to mistype their password locks out the world. The usual escape, `X-Forwarded-For`, is worse than the problem here: any caller sets that header themselves, so trusting it hands the attacker the key to the counter. It is therefore not read, and a test pins that a forged one changes nothing.

It sits **outside authentication** in the strongest sense available: no token has been verified and no password hashed when it runs. And it sits **after** the health probes — a braked liveness probe would take the container out of the load balancer, making the brake itself the outage.

**The counter runs in-process**, so the gateway is pinned to one replica. The way out is a registration change, not a rewrite: `RedisDistributedRateLimitStore` satisfies the same interface.

**Girder's own rate limiter works, and the brake stays anyway — for a smaller reason than before.** Under 3.0.1 there were *three* limiters: two did not brake, the third had no caller, and all three trusted `X-Forwarded-For` unconditionally with no trusted-proxy list anywhere. All of that is fixed. Measured against 4.0.2, same probes the brake is held to:

```
limit 3/min, per origin
  real origin 10.0.0.1                      200 200 200 429 429
  same, with a forged X-Forwarded-For       200 200 200 429 429
  X-Forwarded-For: 127.0.0.1 (the trap)     200 200 200 429 429
  real loopback origin (exempt list)        200 200 200 200 200
  same, exempt list emptied                 200 200 429 429 429
```

The third line is the one that mattered: the header that used to lift the brake without any configuration now changes nothing. `ClientAddress.Of` reads only `Connection.RemoteIpAddress`; a forwarded header works solely behind a named trust list (`TrustForwardedHeadersFrom`). `WhitelistedIps` still defaults to loopback, but that is no longer reachable by forging — only by genuinely coming from there.

**`Bremse.cs` is gone, and the reason it stood is worth keeping.** It survived three rounds of justification, each one measured and each one wrong in a different way: first "Girder's limiters don't brake" (fixed in 4.0.0), then "its rejection is not a problem document" (fixed in 4.1.0), then "it is a global brake and ours is selective". The last one was mine and it was simply false — a limit of `0` writes no counter, so Girder brakes exactly the paths you name. Always could.

What actually kept it was the price: `Girder.Infrastructure` drags **44** transitive packages, and this gateway only routes. `Girder.Http` (4.2.0) carries **none**, and the reason evaporated with it.

The lesson is worth more than the code: **a hand-written replacement outlives its reason.** Each time the stated reason was fixed, a new one was found rather than the code deleted — and every one of them held up until somebody measured it.

**In the twelve services the module stays out for a reason that has nothing to do with Girder and will not change:** a service behind the gateway sees the gateway as the origin — every caller as one. The brake belongs at the entrance, and there it is.

### github-service: the header that made it never work

**Every request needs a `User-Agent`.** GitHub answers *everything* without one with `403` — not a 400, not a message naming the header. Measured 04.09.2026 against the same address: with the header `200`, without it `403`. While it was missing, this service had never worked: neither the gist proof nor the repository fetch, and both surfaced as "github unavailable", which reads exactly like an outage *at GitHub*. `HttpGitHubTests.Jede_Anfrage_traegt_eine_Benutzerkennung` pins it.

**Evidence carries topics and the language SET, never the bytes.** GitHub reports a byte count per language; that is precisely what the deleted package turned into "skill" as `bytes / total_bytes` (ADR-0022 §2). The names are a fact about a repository, the numbers would be the raw material for a statement about a person — so they are not stored, and a test serialises a fetched repository to prove the numbers do not travel even as text. Topics are the strongest evidence of all because they are a *naming*: a human wrote "kubernetes" onto that repository.

**Evidence becomes a suggestion, never a claim.** The profile page offers those words under the skills field; one click fills the form, and only *Save* makes it a naming. That is the bridge the scout needs — the search index knows only what a person typed — and it is two deliberate acts so neither happens by accident.

**Proof runs over OAuth when configured, over a gist otherwise.** `GitHub__OAuth__*` empty means the gist stays, and the button does not appear.

**OAuth needs no name typed first, and that correction was measured.** The naming step was the *gist's* requirement wearing the wrong hat: a gist search must know whose gists to look in. GitHub reports only the account that actually granted consent, so there is nothing to compare and no foreign account to slip in. Measured 05.09.2026 against a real account: the step protected nobody and locked out the person whose connection still carried an older name (the sample data's `sindresorhus`), answering 422 on a correct authorisation. So `POST /me/oauth/start` now *creates* a connection with `Login = null` — "not named yet", a state and not a gap — and `finish` writes in the login GitHub reports. **Where a name was typed it is still compared**, unchanged and for the old reason: otherwise someone names `torvalds`, authorises themselves, and walks away with a proof for a foreign account. Two tests hold the two halves apart, and the comparison has a counter-probe.

`start` is a *command*, not a query, precisely because it creates that row — a query would run outside `TransaktionsBehavior` and the row would never commit. It is therefore called **on click, never on page load**: prefetching it would leave a row for everyone who merely looked at the page. What the page *may* ask on load is `GET /github/oauth` → `{"available": bool}`, a statement about this server's configuration that holds nothing personal and creates nothing.

The one-time string already on the connection is the OAuth `state`. **No scope is requested and the access token is not kept**: it is needed for a single `GET /user`, and what you do not store you cannot lose.

### CI

`.github/workflows/ci.yml` has five jobs: `backend-quality` (restore → build → `scripts/test-dotnet.sh`, in separate steps), `frontend-quality` (check, test, **build**), `e2e` (the Playwright journeys against the full stack), `dependency-audit` (`dotnet list package --vulnerable` + `pnpm audit --prod`), and `images`.

`images` is the one that used to be missing, and its absence is why a Dependabot bump to `node:25` passed CI while breaking the web image. It builds both shipped images, brings the whole compose stack up with `--wait`, then asks three times through the gateway: `GET /jobs` (200 with the `items`/`next` page shape — the proof of routing is *whose* answer it is, not that it succeeded; a missing route would be Ocelot's empty 404, a dead service a 502, the UI would send HTML), `GET /consent/me` (401 plus an RFC 9457 document with `correlationId`), and `POST /auth/register` (201). The middle one exists because `/jobs` went public: it used to carry the problem-document proof on its way past, and afterwards nothing did. Only the write proves the migrations ran; the reads answer on an empty database too.

`--wait` is deliberate. An earlier draft started each service and checked `.State.Running` after three seconds; the counter-probe (take away the gateway's `ocelot.json`) showed a service that throws in `Program.Main` and *keeps running* at 99% CPU, still `Running=true` after 45 seconds. The check was green over a process producing nothing but a stack trace.

`dependency-audit` sets `DOTNET_CLI_UI_LANGUAGE: en` because it *reads* the output, and the same command answers German locally and English on the runner — a grep for one of the two sentences would be silently always-true in the other environment. It also fails if the scan named fewer than 50 projects: a scan that examined nothing is otherwise indistinguishable from a clean result. Neither audit runs with `continue-on-error`; a scan that is allowed to be red gets ignored after the second week, and is then worse than none.

**There is no `dependabot.yml`** (deleted 08.08.2026). This job is the only thing that asks.

**CI is green on the .NET branch as of 09.09.2026** — the first time, and the last green run before it was 13.08.2026, in the Python era. What it actually drove, rather than merely reporting a tick: **966** .NET tests (0 red, 0 skipped), **23** Playwright journeys, **414** route-map answers across the three principals, **79** projects scanned for vulnerable packages, plus both shipped images built, the whole compose stack up, and the chart rendered.

Getting there took four fixes, and **not one of them was in the product code**. Every single one was a place where two sides named the same thing differently, and each stayed invisible until something outside this machine ran it. That is the pattern worth keeping:

**Two lines were missing, and between them they took every job down.** All five jobs red:

- **No `permissions:` block at all**, so the run token carried the account default, which does not include `packages`. Every `dotnet restore` answered `NU1301 … 403 (Forbidden)` for each Girder package in turn — that reads like a broken feed and is a missing line in the workflow. The block now names `contents: read` as well, because an explicit block *replaces* the default rather than adding to it: `packages` alone would take checkout away.
- **`pnpm/action-setup@v4` with no `with:`**, on all three Node jobs. It looks for `packageManager` in the **root** `package.json`, and that file went away when the workspace collapsed into `web/`. Five seconds in: `Error: No pnpm version is specified.` The fix is a pointer (`package_json_file: web/package.json`), never a `version:` here — a second number is the one that stays behind at the next bump.

The pnpm half is **proved**: `frontend-quality` went green on the next run — and with it the open question about **node 25**, which this machine (24) could not answer.

**The `permissions:` block is necessary and not sufficient, and that too is now measured rather than reasoned.** With the block in place the run token still answers `403 (Forbidden)` on every Girder package. `403` is the informative part: the token authenticated and was *refused*, so the missing thing is not a scope but an **entitlement**. **Girder is a private package and this repository is public**, and a package grants access per repository — the run token of `workertransfer` has no claim on a package published from `girder` until somebody says so.

There are exactly two ways to say so, and both are account-level actions no workflow can perform:

- **Package settings → *Manage Actions access* → add `workertransfer`.** The better one: nothing to rotate, and the entitlement is visible where the package lives.
- **A `GIRDER_TOKEN` secret** — a PAT with `read:packages`. It hangs on no repository grant, which is why it goes *first* at all four login points, with the run token as the fallback: `${{ secrets.GIRDER_TOKEN || secrets.GITHUB_TOKEN }}`.

The older comment claimed the run token "reiche dafür". That was an assumption, it was never measured, and it is false. **A pull request from a fork cannot build here under either fix** — it gets no secrets and no entitlement to a private dependency.

Measured locally while the workflow was being repaired: both audits are clean (79 projects named, no vulnerable packages; `pnpm audit --prod` finds none), and the frontend gate passes end to end (`tsc` clean, 150 tests in 20 files, `vite build` green). So once the entitlement stands, what remains untested by anything but CI is the .NET half.

**And behind the entitlement stood two more, both of the same family: something was read in a place that spelled it differently.** The `GIRDER_TOKEN` secret cleared the restore — `dependency-audit` went green — and uncovered them.

- **`scripts/test-dotnet.sh` read its own output in German.** It grepped `^(Bestanden!|Fehler!)` and `erfolgreich:`; the runner answers `Passed! - Failed: 0, Passed: 966, Skipped: 0`. All sixteen suites reported *"keine Ausgabe — die Reihe lief gar nicht"* while in fact running and passing — Identity took two minutes and still counted as never started. The guard was right to be loud; it was reading the wrong language. `DOTNET_CLI_UI_LANGUAGE=en` is now exported **in the script**, not in the workflow: whoever reads the output owns its language, or the result depends on who called. The same trap `dependency-audit` already documented, one directory over.
- **The docker secret file was named with `${{ env.HOME }}`, and the `env` context has no `HOME`.** It holds only what an `env:` block declares, so the expression became empty, the path read `/.nuget/NuGet/NuGet.Config`, and BuildKit answered `##[warning]secret file not found` — a *warning*. The build carried on with no credentials at all and died two hundred lines later on `401 (Unauthorized)`. **That is how to tell the two apart: 403 is authenticated-and-refused, 401 is never authenticated.** Both ends now say `${{ runner.temp }}`, and a missing token is named on the spot instead of surfacing as a broken feed — the check reads the *shape*, never the value.

### Branches and the one path back

`feature → develop → main`, never a feature branch straight into main.

A **hotfix** is the single exception: it branches off `main`, merges into `main`, and never passes through develop. `.github/workflows/backmerge.yml` carries it back, and its trigger is deliberately narrow — a *merged* PR into main whose head branch starts with **`hotfix/`**. The name is load-bearing: call it `fix/…` and the run never fires, so the correction silently stays out of develop. It pushes to develop only on a fast-forward; otherwise it prepares `hotfix-rueckweg/main-nach-develop` and puts a compare link in the run summary. A real conflict turns the run **red** rather than reporting green while the fix is missing.

## Architecture

### Layering (Clean Architecture, inward-pointing dependencies)

One project per layer, per service, without exception:

```
<name>.Api  ->  <name>.Application  ->  <name>.Domain
                        |                    ^
                        └-- <name>.Infrastructure --┘
   <name>.Contracts — versioned DTOs at the boundary
```

Repository *interfaces* live in Domain, implementations in Infrastructure. Commands, queries and handlers live in Application. **All wiring is behind one `Add<Service>Infrastructure()`** per service — its composition root (ADR-0003, and deliberately not a fluent `PlatformBuilder`).

`ServiceDefaults.AddWorkerTransferDefaults()` is the one call every service makes. What is in it is there because twelve services answering it twelve ways would be twelve chances to answer it wrong: how a token is verified, what a failure looks like on the wire, the order of the pipeline. What is *not* in it is everything that is a decision — which database, which repositories, whether there is a cache, whether there is an outbox. Those stay in each service's own composition root, where a reader can see them.

### CQRS

Mediator via Girder's `AddCQRS`, but with **our own `IBefehl` / `IAbfrage` markers** over MediatR's `IRequest`, defined per service in `Application/Nachrichten/`. This is deliberate: Girder's `ICommand<T>` carries its own error envelope, which would be a second one beside RFC 9457, and two shapes for "what went wrong" is exactly the divergence that later gets papered over in the UI. `TransaktionsBehavior` wraps **commands only** — a query has nothing to commit.

### Errors

RFC 9457 problem documents, everywhere, from `ServiceDefaults.ProblemDetailsMiddleware`. Every response carries a `correlationId`. **No values in logs** — shapes, not contents. Girder logs no values either, neither redacted nor scrubbed; do not build that back.

**Girder 4.4.0 masks log properties by exact name, and here it has nothing to do — measured, not hoped.** The running stack shows `[REDACTED]` zero times. That reading would be ambiguous on its own, so `MaskierungTests` supplies the other half: `Username`, `Email` and `City` *are* redacted, `SecretName` and `TokenId` are visible again (the 4.3.0 substring bug), the comparison is exact (`Emailvorlage` survives), and our own German property names stay readable. The reason nothing goes dark is twofold — our templates are German against an English list, and Girder's `LoggingBehavior` writes shapes anyway: `Shape of RegistrierenBefehl: {Email: string(34), …}` is a length, never an address. **Naming a log property `Email` would make it unreadable**, and that test says so.

An endpoint filter that has already written a response must return `Results.Empty`, never `null`: with headers sent, the framework writes a JSON null after them, which tears the connection on a POST-with-body and is invisible on a GET except in the log.

### Request context

Correlation and tenant flow through the request, set by middleware in `ServiceDefaults`. **Tenant identity must never come from a browser header in production.**

**A tenant is a company, and a natural person has none** (ADR-0017). This is modelled as `Capacity`: `AsSelf` or `ForCompany(TenantId)`. Tenant is an *optional* attribute of a principal, carried only by company-based features (adverts, employer accounts, recruiting teams). It is **not** the scoping axis for personal data: user data is scoped by subject identity, and both axes coexist. The consent ledger therefore has no tenant column by design — a consent belongs to the person and follows them across employers. Do not "fix" that by adding one.

Company membership is a relation, not a column on the user — one person may act for several companies (ADR-0018). Email is globally unique. `POST /auth/login` returns a person token with **no** tenant claim; `POST /auth/company/{id}` verifies membership and only then mints a token carrying the tenant. So the client names the company but the server decides, and the tenant in the token never came from client input. `null` means "acted as a person", not "missing".

The access token carries `sub`, `email`, `jti`, `iat`, `exp`, `iss`, `aud`, `session_id`, the long-form `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` (a duplicate of `sub`), and — only while acting for a company — `tenant`. **Nothing else.** During the migration it also carried `tenant_id` and `type`; both are gone.

That ninth claim was undocumented until it was **measured against the running stack on 03.09.2026** — this file said "eight, nothing else", the wire said nine. It stays, and the reason is Girder's: `MapInboundClaims = false` switches off the framework's own derivation, and seventeen readers resolve the caller through that name, two of them in provider packages that cannot see Girder's assembly. Dropping it saves seventy bytes and turns every one of those into a silent `null`.

The lesson is the test, not the claim: `TokenformTests` forbade two *names* and would never have noticed a ninth. It now pins the **complete set** in both capacities — a set comparison, not a membership check. Roles and permissions are not in the token: they are read from the membership table per operation, so the token was never authoritative.

**Two suites, because one of them could not have caught it.** `TokenformTests` calls Girder's issuer directly; `TokenformAmDrahtTests` goes the whole way — register, confirm, sign in, switch to a company — and reads the token where a browser gets it, out of the `Set-Cookie`. Between the issuer and the wire sit the service's settings, its wiring and the cookie, and a claim added by *that* layer would be invisible to the first suite. The endpoint suite also pins something the unit suite cannot: **the token never appears in a response body.** It leaves only as the `httpOnly` cookie, so no script in the browser can read it, and there is no second path to the same string for someone to log by accident. Re-measured at the running stack on 04.09.2026: nine as a person, ten for a company, both matching.

`AuthMiddleware` resolves the principal from an `Authorization: Bearer` header **or** the `access` cookie, in that order. Both carriers are needed: service-to-service and CLI callers send the header; the browser never sees the `httpOnly` token and can only replay it as a cookie. Any new service verifying identity tokens must accept both.

**A company is created at registration and nowhere else.** `POST /register` takes an optional company name; the intent is stored on the pending user and redeemed by `POST /auth/verify-email`, in the same transaction. The order is forced, not chosen: creating a company requires an *active* account and derives the domain from the **confirmed** address, so it is proven before the company exists and cannot be forged (ADR-0019).

Two checks sit in two places on purpose. **Freemail is rejected at registration**, and **before** the existence check — otherwise a *known* freemail address would get the silent "ok" and an unknown one a 422, and that difference is exactly the enumeration channel `/auth/register` closes. **"Domain already claimed" is checked only at confirmation**: earlier it would answer *"is firma.de on this platform?"* for anyone who guesses a domain. That creates a state which did not exist before — **account confirmed, company refused** — and `/verify` says so; the confirmation itself must never fail because a name was taken.

**There is no "create a company" button and no `/company/new` route.** Someone already registered as a person joins a company by **invitation**, which is also the right answer when their domain is already claimed — they have colleagues there.

**`admin` vs `member` is enforced since PBI-2, and until then it was not — anywhere.** The tree held **zero** `RequirePermission` outside identity-service: the navigation *hid* company entries and the server answered 403 only where somebody had thought of it. `Mitgliedschaftsrecht` read the role per request from the membership table — and nobody asked. **Hiding is not access control**, and that sentence is the whole reason this was the largest real gap in the tree.

Eight routes now demand an `admin`, and the line behind them is written down once so the next route does not reinvent it: **`admin` is whoever binds or changes the company, or who belongs to it; `member` is the daily work in the company's name** — reading, drafting, writing, approaching, processing applications. **In doubt, `member`:** a right that is too narrow turns an invitation into a spectator seat, and then somebody creates a second admin in order to work — and "admin" is a word in a table again.

| route | why `admin` |
|---|---|
| `POST /companies/{id}/invitations` | who belongs to the company |
| `DELETE /companies/{id}/invitations/{id}` | the reverse side of inviting |
| `DELETE /companies/{id}/members/{id}` | who belongs to the company |
| `POST /jobs/{id}/publish` | the advert starts representing the company |
| `POST /jobs/{id}/close` | it stops — and with it the chance to apply |
| `PUT /companies/me/profile` | the shop window, public and for everyone |
| `POST /transfers/{id}/offer` | start date and placement fee |
| `POST /transfers/{id}/complete` | states that both hold |

Three of those already had the check in the *handler*; moving it to the endpoint moved it **before** the body is looked at — otherwise a stranger learns from the difference 400/403 that their body was fine.

**Where the role comes from is the part that carries the design.** Not the token: a token outlives the removal, and the removal would then take effect on expiry — at exactly the operation where *immediately* is the only thing that counts. identity-service answers `GET /internal/companies/{id}/members/{sub}/role` behind the shared secret; `ServiceDefaults.Rollen.Adminrecht` asks it per protected request, with no cache, for the same reason a consent must take effect on the next read (ADR-0013). The price is one lookup per protected request, and it touches only the operations that bind a company — never the reads people use all day.

**Two things that are easy to get wrong here.** The tenant comes from the **token** in the ten services (`/jobs/{id}/publish` names no company) and from the **path** in identity (`/companies/{id}/…`) — that is why there are two handlers and not one. And **a silent role lookup answers 503, never 403**: the handler can only say "yes", so it leaves a note on the `HttpContext` and `Ablehnungsgestalt` turns the refusal into 503. A 403 would read as "your right was taken away", and nobody would go looking for an outage.

**`Identity__Adresse` and `Identity__Geheimnis` are therefore load-bearing configuration** in jobs, companies and transfer (applications and notification already had them). Without them every protected route answers 503 — correctly, and visibly.

Do not mistake the navigation for access control.

**Registration is open to any email address, private ones very much included** — the transfer market's normal user is a person with no company. The freemail blocklist applies at exactly one place: claiming a domain. Accounts start pending and are activated by a mailed token; register and resend answer identically whether or not the address is known, because a differing answer would reveal platform membership without asking the ledger.

### Persistence

EF Core, one `DbContext` per service, one database per service — **no shared database and no cross-service repository abstraction** (ADR-0004). Migrations live under `src/<service>/…Infrastructure/Persistence/Migrations` and are applied at startup by `ServiceDefaults.Wanderung`, which retries **only transient failures** for one minute. That narrowness matters: an earlier version caught everything, and a schema error became a minute of retries ending in a misleading `type … already exists`.

**Aggregates come back detached from the repositories.** A mutation reaches the database only via an explicit save. Forgetting it costs nothing at test time and silently loses the write in production.

**And the save itself needs `.AsTracking()`, because every context runs `QueryTrackingBehavior.NoTracking`.** A repository that reads a row, assigns to it and relies on `SaveChanges` writes *nothing* — the row came back detached, the assignments go nowhere, and the caller still gets its `204`. Measured 09.09.2026 on `PUT /resumes/me/documents/{id}/as-cv`: 204 returned, the kind stayed `sonstiges`.

**The insert path hides it**, because `Add` always tracks. So it surfaces only at the first *mutating* caller, which may arrive months after the repository was written — `EfUnterlagenSpeicher` was the only one of nineteen without `AsTracking()`, and had no mutating caller until then. When you write a `SichereAsync`, the read inside it takes `.AsTracking()`; when you review one, that is the line to look for.

`Personenzeile` is an EF annotation put on tables whose *key* is the person (`profiles`, `portfolios`, `github_connections`, `resumes`, `notification_preferences`, `market_status`). It exists so the erasure guard can recognise them: those tables have no `subject_id` column, because the id *is* the subject, and a guard looking only for column names would miss exactly the tables that hold the most.

### The outbox

`WorkerTransfer.Outbox` records an *intent* in the **same transaction** as the domain change; a dispatcher delivers it with retries (ADR-0025). The promise it keeps is the old one — a failed mail must never topple the transaction — kept differently.

The table deliberately holds **no content**, only a user id and a kind. An outbox is durable storage and ends up in every backup, so a payload column would be an invitation to write message text into it. **Never carry an email address into it.** Giving up means leaving the row, never deleting it. Delivery is **at-least-once**. There is no broker, and none is planned.

### The Girder modules

**Every service reports its own composition at startup, by name**, and that report is the only thing that catches a drift between intent and default. Measured 09.09.2026 on 4.4.0, consent-service:

```
Girder für consent-service: 19 Module in Betrieb (Logging, HttpContextAccess,
JsonOptions, Jwt, SecurityMonitoring, Resilience, SecretManagement, Audit,
InputSanitization, HealthChecks, Caching, Observability, SecurityHeaders,
Authorization, CorrelationPropagation, ApiDocumentation, Cors, Principal,
SovereignPlatform), 6 ausgelassen
```

**It reports names rather than a count, and that was itself a measurement.** The same `19` stood before and after a new `AddSovereignPlatform` line — the number could not show the difference it existed to report.

**The report contradicts the table below, and the report wins.** Five modules run that the table calls "bewusst nicht": `SecurityMonitoring`, `Resilience`, `SecretManagement`, `Audit`, `Caching`. That is not a regression — the table describes the 3.0.1-era `InfrastructureBuilder` *methods*, and those decisions were about hand-wiring them one by one. `UseDefaults()` in 4.4.0 brings them, and taking the defaults was the later, deliberate decision (a module runs unless there is a measured reason against it). **The reasons in the table still describe what each area does; they no longer describe whether it is switched on.** Whoever revisits one of them starts from the running report, not from the table.

Our composition root takes `UseDefaults()`, adds `Principal` and `SovereignPlatform` (plus `PasswordHashing` and `TokenSessions` for identity only), and declares **six** exclusions with a reason each: `RateLimiting`, `HttpResponseCaching`, `Communication`, `Encryption`, `ResourceAuthorization`, `PermissionEnforcement`.

**The chain now honours those exclusions too**, which it did not before 4.1.0. `UseWorkerTransferDefaults` used to carry a thirteen-line copy of Girder's default chain with three lines left out — not a design, a workaround: the chain called every step unconditionally and `UseRateLimiting()` aborted startup. It reads `GirderComposition` now, so `app.UseGirder(environment, dienstname)` is the whole thing and a `Without(...)` takes effect once instead of twice. The two exclusions that are real decisions are `RateLimiting` and `PermissionEnforcement`, both topology; the other four record decisions about modules that are not in the defaults anyway.

`PermissionEnforcement` is new in 4.1.0 and exists because of us: `Authorization` used to be one module for two things — the policy provider that answers `[RequirePermission]`, and the middleware that refuses *everything else*. A service with a public surface had to choose between an authorization system that answers nothing and a blanket 401. We keep the provider (the company permissions hang off it) and leave the middleware out.

The rule behind the table is inverted from what it used to be: **a module is called unless there is a measured reason against it**, and the reason lives in `.Without(module, reason)` in the code — `GirderBuilder` refuses an empty one.

*(The table below is from the 3.0.1 era and names `InfrastructureBuilder` methods, not the module enum. It still records why each area was rejected, but the wiring it describes is gone: the migration to `AddGirder(...)` with `UseDefaults()` is H1 in `docs/AUFTRAG-UMSTIEG-4.md`, and the four measurements that revisited those decisions are H2 — written up in `docs/erkenntnisse-girder.md`. Where the two disagree, the measurements win.)*

Two rules that produced most of the corrections here:

- **Search for the purpose, not the part.** Twice a conclusion was drawn from one implementation when Girder had several. `grep -ril <purpose>` over Girder's `src/` first, look at *every* hit, then decide. A broken path proves nothing about the others.
- **Read the source, not the name.** Three of the lines below say something different from what the method is called.

| Modul | Stand | gemessen |
|---|---|---|
| `AddJwtAuthentication` | **gerufen** | Prüft Token in jedem Dienst. Identity stellt aus (`AlsAussteller`), alle anderen prüfen nur. |
| `AddPrincipal` | **gerufen** | Baut `ICurrentPrincipal` aus dem geprüften Token — die Grundlage von `Capacity` (ADR-0017). |
| `AddSecurityHeaders` | **gerufen** | Setzt die Sicherheitsköpfe vor allem, was einen Rumpf schreibt. |
| `AddHealthChecks` | **gerufen** | `/health/live` und `/health/ready` in jedem Dienst. Das Gateway hat eigene — siehe unten. |
| `AddObservability` | **gerufen** | Ablaufverfolgung und Kennzahlen; Jaeger hängt daran. |
| `AddPasswordHashing` | **gerufen** | Nur identity-service, über `AlsAussteller()`. BCrypt (ADR-0006). |
| `AddTokenSessions` | **gerufen** | Nur identity-service. Sitzungen, die ein Widerruf erreichen kann. |
| `AddAuthorization` | **gerufen (identity)** | Bringt den `PermissionPolicyProvider`, der `Permission:*`-Richtliniennamen zur Laufzeit auflöst — ohne ihn beantwortet sie niemand und das Gerüst lehnt **jede** Anfrage an einen so geschützten Endpunkt ab. Seine Berechtigungsrichtlinien lesen aus **Ansprüchen**; unser Token trägt keine. Deshalb steht ein eigener Handler daneben (`Mitgliedschaftsrecht`), der die Rolle je Anfrage aus der Mitgliedschaftstabelle liest: ASP.NET führt alle Handler aus, ein `Succeed` genügt. Rechte ins Token zu legen wäre kürzer und schlechter — eine Entfernung wirkte dann erst beim Ablauf. |
| `AddInputSanitization` | **Modul UND Middleware laufen — seit 4.1.0, und ohne die Rumpfpruefung** | Bis 4.0.2 war das die schlimmste Zeile der Tabelle: die Middleware traf das *bloesse* SQL-Wort an einer Wortgrenze, und ein Bindestrich ist eine. Gemessen **400** auf `Union-Investment` (eine echte Fondsgesellschaft), `Select-Kundenberater`, `Drop-In-Zentrum`; und weil jede Anfrage der Loeschseite `Referer: …/delete-account` trug, war **`/delete-account` vollstaendig tot** bis hin zu ihrem `/auth/session` — die Seite erfuhr nicht, wer angemeldet ist, und bat eine angemeldete Person, sich anzumelden. In JSON-Ruempfe sah sie dabei gar nicht hinein. **4.1.0 behebt beides**: erkannt wird Injektionssyntax statt Woertern, der `Referer` ist keine Eingabe, und JSON-Zeichenketten werden geprueft. `StellenreiseTests.Ein_Suchbegriff_mit_einem_SQL_Wort_wird_beantwortet` ist gruen. **Die Rumpfpruefung stellen wir trotzdem ab** (`InspectJsonBodies = false`, begruendet in `Dienstgrundlage.cs`): sie laeuft vor allem anderen und kann kein Feld benennen, also wird aus einem 422 mit Feldnamen ein blankes 400 — gemessen an fuenf Faellen in drei Diensten. Abgewiesen werden sie weiterhin, nur sagt die Antwort dem Menschen im Formular weniger. Query-String und die zwei Adresskoepfe bleiben geprueft. |
| *(Validierung, kein Modul)* | **läuft schon, hat aber nichts zu tun** | `AddCQRS` hängt `ValidationBehavior` **bereits** in jede Pipeline und ruft **bereits** `AddValidatorsFromAssemblies`. Das Verhalten steigt sofort aus, wenn es keine Validatoren findet — und wir haben **null** `AbstractValidator` geschrieben. Es fehlt also keine Verdrahtung, sondern der Inhalt. Achtung beim Schreiben: `ValidationBehavior` **protokolliert die Fehlermeldungen**, eine Meldung muss deshalb die Regel nennen und nie den Wert. |
| `AddResilience` | **bewusst nicht** | Registriert nur `ICircuitBreakerFactory` und `IRetryPolicyFactory` — es umhuellt **keinen** HttpClient. Der alte Grund gegen `AddResilientHttpClient<T>` ist weg: seit 4.0.0 ist ein Nicht-2xx keine Ausnahme mehr, und `ResilientHttpPolicyHandler` wiederholt nur noch, was ein zweiter Versuch anders beantworten koennte (Transportfehler, Zeitueberschreitung, 408/429/5xx) — gemessen, jeder Status kam als er selbst an und **einmal**. Es bleibt draussen, weil es nichts mehr zu holen gibt: das echte Risiko lag woanders und ist behoben — **alle fuenfzehn Aufrufstellen setzen ein eigenes Zeitlimit**, sieben taten es nicht und liefen in die 100-Sekunden-Vorgabe von `HttpClient`. `ZeitlimitTests` haelt das fest. |
| `AddCommunication` | **bewusst nicht — und der Umstieg ist abgesagt, nicht vertagt** | Drei gemessene Gruende, alle an 4.0.2 ohne Fremdcode. **1.** `CommunicationModule` deklariert `RequiresProvider<IEventBus>`, und `IEventBus` registriert in ganz Girder genau eine Stelle (`Girder.Messaging.MassTransit`) — ohne Broker stirbt der Container beim Aufloesen des Managers. **2.** `UseGateway` steht per Vorgabe auf `true`: dann geht *jeder* Aufruf an den Dienst namens `gateway`, der uebergebene `serviceName` wird fuer die Adresse nicht gelesen — Dienst-zu-Dienst-Verkehr liefe durch Ocelot und durch unsere eigene Bremse. **3.** `EnableResponseCaching` steht per Vorgabe auf `true`, Vorgabepolitik „jede GET-Antwort, fuenf Minuten"; unsere Einwilligungspruefung entkaeme dem nur durch ihre **Form** (POST), nicht durch eine Entscheidung — und ein Ledger-Ergebnis, das fuenf Minuten liegen bleibt, waere ADR-0013 ins Gesicht. **Und der Grund, der den Umstieg attraktiv machte, ist weg:** die Korrelationskennung reist seit 4.0.0 an jedem `HttpClient` der Fabrik (ueber einen echten Sprung nachgewiesen). Wir bekommen die Kette, ohne die Antwort aufzugeben. |
| `AddDistributedRateLimiting` | **bewusst nicht — aus Topologie, nicht aus Misstrauen** | Sie ist seit 4.0.0 in Ordnung, und das ist ohne Fremdcode nachgemessen: `ClientAddress.Of` liest allein `Connection.RemoteIpAddress`, ein gefaelschtes `X-Forwarded-For: 127.0.0.1` hebt sie **nicht** mehr auf (weitergereichte Koepfe wirken nur ueber `TrustForwardedHeadersFrom`), sie zaehlt je Herkunft, kann Je-Pfad-Grenzen und setzt `X-RateLimit-*` und `Retry-After`. Draussen bleibt sie trotzdem, und der Grund ist endgueltig: **ein Dienst hinter dem Gateway sieht als Herkunft nur das Gateway** und damit alle Aufrufer als einen. Gebremst wird am Eingang — und dort ist es seit 4.2.0 genau dieses Modul, aus `Girder.Http` statt aus einem Eigenbau. |
| `AddCaching` | **bewusst nicht** | HTTP-Antwort-Caching plus `CacheInvalidationService`, und es verlangt einen `IDistributedCacheService`. Eine Einwilligung muss sofort wirken (das gilt, weil es richtig ist, nicht weil ein ADR es sagt) — was zwischengespeichert wird, muss deshalb einzeln entschieden werden, nicht global eingeschaltet. |
| `AddAuditLogging` | **bewusst nicht** | Registriert `ISecurityAuditLogger`, der ins **Protokoll** schreibt; `AuditBehavior` in der Pipeline wirkt auf `IAuditableCommand`, das wir nicht umsetzen. Unsere Prüfspur ist eine **Tabelle** (`EfPruefspur`) in derselben Transaktion wie die Änderung: sie ist Beleg, Girders ist Telemetrie. Beides kann nebeneinander stehen — nur ersetzt keins das andere. |
| `AddEncryption` | **bewusst nicht** | Verlangt `IDataEncryptionService` **und** `IMasterKeyProvider` — beide kommen aus `Girder.Redis` oder einem Geheimnisspeicher. Wir verschlüsseln heute auf Feldebene nichts; wer damit anfängt, entscheidet zuerst, wo der Hauptschlüssel liegt, und das ist H2. |
| `AddSecretManagement` | **offen, gehört zu H2** | Geheimnisverwaltung samt Rotation. Genau die Frage, die H2 stellt — erst `.env` und die Rangfolge, dann entscheiden, ob dieses Modul den Platz von Infisical einnimmt oder daneben steht. |
| `AddSecurityMonitoring` | **bewusst nicht** | Alarme und Bedrohungssignale, verlangt einen `IDistributedCache`. Ein Alarmweg ohne Empfänger ist ein Protokolleintrag mehr; das lohnt erst, wenn jemand ihn liest. |
| `AddResourceAuthorization` | **offen** | Ressourcen- und Eigentümerprüfungen. Die Richtlinien stehen jetzt; ob dieses Modul darüber hinaus etwas trägt, ist ungemessen. |

**Nicht in dieser Tabelle, weil kein Modul:** `AddCQRS` (aus `Girder.Application`) ruft jeder Dienst selbst, und `worker`-eigene Pipeline-Glieder (`TransaktionsBehavior`, identity zusätzlich `VersandBehavior`) hängen daneben.

### `SovereignPlatform` — angenommen, und die Hosts kommen aus der Konfiguration

New in 4.4.0 and taken, but only after a measurement that came close to costing the platform. `AddSovereignPlatform` bundles four things: the egress boundary, the log masking, the sovereignty report and an audit trail. The **egress guard hangs on every client from `IHttpClientFactory` and *refuses*, it does not log** — and measured before registering anything (`EgressTests`, in the cross-cutting suite):

```
default "loopback + RFC1918"
  http://consent-service:8002   → REFUSED   ← a name is not an address
  https://api.github.com        → REFUSED
  http://10.0.0.5:8002          → allowed   ← the other half
```

Adopted naively that is every service-to-service call gone, **in the stack and not in the tests**, because the tests use fakes rather than real clients.

So the allowed hosts are **derived from configuration** and never listed in code: `Consent__Adresse`, `Jobs__Adresse`, `Auskunft__*`, `Erasure__Adressen__*` are already in the environment, and what a service calls it has said there. A second list would be the one that goes stale first — and nobody would notice, because the call is simply refused. `Draft__Adresse` and `GitHub__Adresse` therefore moved out of the source and into compose: a built-in destination abroad is exactly what a sovereignty report exists to surface.

**Und was in der DATENBANK steht, sieht sie nicht — das kostete den Anschreiben-Agenten.** The AI access a person sets up (`KiZugang`: provider, address, model) is a row, not configuration, and a local model is an intended case — `Anschreiber.cs` says so: *"gemessen an Ollama"*. Measured 10.09.2026: every cover-letter job answered `EgressDeniedException: Outbound call to 'host.docker.internal' is not allowed`, the worker started, never wrote, and **the UI showed nothing at all** — the failure died in a background worker where no one looks.

So the *operator* declares where a person may point, in `Draft__ErlaubteZiele__*`, and the person chooses within it. That needs no code: the derivation takes the host from any configuration value that reads as an absolute http URL, and the guard compares hosts, not ports. It is not a concession to the boundary but its point — without such a declaration any person could send their requests to any server at all. The counter-check that this actually holds is the real path, not the absence of errors: `Modell qwen2.5-coder:7b über openai_compatible` → `erstes Bruchstück` → `Modell fertig (957 Zeichen)`.

**The gateway is exempt** — it never calls `AddWorkerTransferDefaults`, so no guard runs there and Ocelot's routing is untouched. Measured rather than assumed; otherwise the `Host`/`Port` pairs from `ocelot.json` would have had to be covered too.

**Girder's audit trail comes along and is refused, as a mechanism rather than a comment.** There is no `WithoutAuditTrail()`; the bundle is one module. Girder's fallback sink writes to a list in the process — it survives no rollback and sits *beside* our transaction instead of inside it, so it does not satisfy ADR-0012, which `EfPruefspur` does. `VerweigerndePruefspur` is registered as the sink: never called, and whoever does call it gets a sentence saying where to go instead. A silent store that looks like an audit trail would only be noticed when somebody needs the trail and finds it empty.

### Und dieselbe Frage an unseren Eigenbau

| Eigenbau | bleibt? | gemessen |
|---|---|---|
| `Bremse.cs` | **GELÖSCHT (4.2.0)** | Drei Begründungen nacheinander, jede gemessen, jede anders falsch — zuletzt „Girders ist global, unsere selektiv". Das war schlicht unwahr: eine Grenze von `0` legt keinen Zähler an. Gehalten hat sie am Ende nur der Preis: `Girder.Infrastructure` zieht 44 Fremdpakete für ein Gateway, das routet. `Girder.Http` zieht null. |
| `Korrelation.cs` (Gateway) | **GELÖSCHT (4.2.0)** | Girders Zwischenschicht schrieb die Kennung bis 4.0.2 nur in Antwortkopf, Gepäck und `Items`; ein Reverse Proxy reicht aber nur **Anfrage**köpfe weiter. Behoben in 4.1.0, und seit 4.2.0 kostet ihr Bezug nichts mehr. |
| `Gesundheit.cs` (Gateway) | **ja** | Ocelot beendet die Kette, ein `MapGet` dahinter läuft nie — gemessen. Girders `AddHealthChecks()` registriert Endpunkte, keine Middleware. |
| `Navigation.cs` | **GELÖSCHT (11.09.2026)** | Das Gateway liefert keine Oberflaeche mehr aus (ADR-0040). |
| `ProblemDetailsMiddleware` | **zu prüfen** | Girder hat `GlobalExceptionHandlingMiddleware` mit eigener Fehlergestalt. Eine Gestalt über alle Dienste ist der Grund für unsere — zu belegen, dass Girders nicht dasselbe kann. |
| `Outbox` | **ja** | Girder hat keine. |
| `Wanderung`, `ZugriffsCookie`, `Skills`, `Contracts.*` | **ja** | Fachlichkeit, kein Nachbau. |

## The route map is a test, not a checklist

[`docs/routenkarte.yml`](docs/routenkarte.yml) records, for every endpoint reachable through the gateway, what it answers in **four** principals: no token, a person with no company (`tenant_id` null), someone acting for a company as `member`, and the same as `admin`. The second one is the one people forget — on a transfer market a person without a company is the *normal* case, and the rows where it differs from the two on the right are exactly where ADR-0017 is doing work.

**The fourth column is what made PBI-2 checkable at all.** Before it the map could not express "admin, not member", so nothing measured it — and nothing was enforced. Five rows show the difference; the three identity routes cannot, because the company id in their path does not exist and *both* company principals get 403 there, for two different reasons. `UnternehmensreiseTests` covers those three against a company that exists, and `Die_vierte_Spalte_unterscheidet_admin_von_member` pins the five by name: opening one of them is then a deliberate edit, not a silent one.

Two things drive it, and they answer different questions:

- **`RoutenkarteTests`** (in the gateway suite, no stack needed) asserts the map is **complete**: every route in `ocelot.json` has at least one entry. Add a route without an entry and it goes red. Without this, the map would be correct exactly until the next endpoint.
- **`scripts/routenkarte.sh`** (`make routenkarte`, needs `make up`) drives every answer against the running stack — four columns since PBI-2, and the fourth principal is built the way a person is: registered, invited with `role: "member"`, joined. The script refuses to measure unless that account really carries the company's tenant *and* the role `member` — without that check a failed join would quietly measure a person without a company, and all eight protected rows would still look green, because 403 is the right answer there for a person too. It is a script rather than a test suite on purpose: a suite that needs a stack skips itself without one, and a skipped test looks exactly like a passing one.

The reason it is a test at all: `scripts/k8s-up.sh` claimed `GET /jobs` answers 200 at a time when it answered **401**, and the script had never run, so nobody found out. (It answers 200 again today, but for a reason that was decided rather than assumed — see the map's own note.) A list nobody drives is wrong the day after it is written.

Building the map found three things a checklist would have blessed: five endpoints answered **500** on a malformed body (one of them `/auth/login`, where a missing password reached the password hasher); `/companies/withdrawal` was routed publicly and answered 401, confirming an internal door exists; and `GET /notifications` answers **405**, which reveals a path the service deliberately hides behind a 404. The first two are fixed. The third is written down in the map, not fixed.

### The gateway

Ocelot 25. `ocelot.json` carries its own reasoning in `//` comments — measured: .NET's JSON configuration provider skips them.

**The gateway serves the API and nothing else.** The UI is the Vite dev server on `:5173` in compose; the browser loads it from there and calls the gateway on `:8090` for every request. Two origins, and CORS names both (`CORS_ORIGINS`). Confirmation mails point at `WORKERTRANSFER_WEB_URL`, which is the UI — not the gateway.

It was the other way round until 11.09.2026: a `Navigation` middleware rewrote any request carrying `Sec-Fetch-Dest: document` onto a `/__ui/{alles}` route that proxied to the `web` container, so `…:8090/jobs` answered with the page while `fetch` got the resource. That is gone, deliberately (ADR-0040): one address meaning two things is a rule nobody remembers at the next endpoint, and it made the dev server a proxy target, which broke HMR through the gateway.

**The cost is real and was accepted:** the Helm chart exposes only the gateway (`publicUrl`, `gateway.nodePort`), so `make k8s-up` now brings up a cluster with no reachable UI. Whoever wants staging back gives `web` its own entry and splits `publicUrl` into a web origin and an API origin.

Route order in `ocelot.json` is pinned by `ReihenfolgeTests` against a *reversed* fixture. A probe that merely set all priorities equal stayed green because file order happened to be right; only reversing the order exposed it, and eight routes fell.

### Frontend

`web/` was **rebuilt** (the hand-over notes are in `docs/uebergabe/`): MUI 9 with Emotion, Redux Toolkit, `react-router-dom` with `createBrowserRouter`, laid out per feature — `core/{api,router,store}`, `features/<name>/{components,pages,store,types}`, `shared/`. Colours, spacing and type live in `src/styles/tokens/` as the single source; `src/styles/theme.ts` builds the MUI theme from them, light and dark. A colour literal in a component is a defect, not a shortcut.

**The palette deliberately drops green.** On a platform that decides about consent, green *is* a signal ("granted") and must not also be the house colour — mixing the two takes the signal's meaning away. Indigo carries, amber accents sparingly, and green/red/amber stay free for granted, revoked, in progress.

The former stack (TanStack Query + TanStack Router, `packages/ui` with hand-written CSS) is **gone** — all thirty routes moved, and the old `src/routes/` tree with its sixteen feature folders went with it. One rule survived the rebuild verbatim: **a consent toggle is a switch, never a checkbox** — a checkbox promises the change applies on submit, and for a consent toggle that difference is not cosmetic. `shared/components/ui/ConsentSwitch` is the one place that renders it.

**Every route path is English — no German, no mix**: `/`, `/overview`, `/login`, `/register`, `/verify`, `/invitation`, `/profile`, `/portfolio`, `/resume`, `/consents`, `/settings`, `/delete-account`, `/market`, `/transfers`, `/scout`, `/jobs`, `/careers/<slug>`, `/applications`, `/my-data`, `/github`, `/company/team`, `/company/jobs`, `/company/profile`, `/company/transfers`. `/` is the marketing page and redirects a signed-in visitor to `/overview`; before that split one address served both, which left the overview unlinkable and made a screenshot of `/` depend on the session.

**Every text a person reads comes from a catalogue** (ADR-0031). Three languages — German, English, French — in `web/src/core/i18n/kataloge/{de,en,fr}.ts`; German is the source and the other two are translations of it. Three guard tests hold them together: same keys, actually translated rather than copied, no empty string. The per-language exemption list for genuine cognates ("Status", "Website", "Administrator") is short and each line is an individual case — a French entry never rides along on an English one.

**The API layer's error messages are keys, not sentences.** `shared/api/fehler.ts` maps a status code onto a reason and a catalogue key, and `deuten()` resolves it *when the answer arrives* — a table built at module scope would freeze the language before anybody could choose one. The key type is derived from the catalogue (``Fehlerschluessel = `fehler.${keyof Katalog["fehler"]}` ``), so a German sentence left behind at one of those sites is a compile error rather than a string i18next silently hands back unchanged.

**The language lives on the account, not on the request** — `users.language`, set once at registration from `Accept-Language` and never read from a header again. The reason is the mail: a deletion confirmation is written when the last of eight services acknowledges, days later, by a dispatcher with no browser and no header. The outbox stays content-free (ADR-0025); the language is read from the row at delivery. `PUT /account/language` refuses an unsupported tag with 422 instead of quietly storing German.

**A problem document stays English on the wire** and describes a *shape* (`"malformed request body"`, `"invalid: email, password"`). The UI translates by status, so a person reads their own language in every language the UI knows — including ones the backend never heard of. Threading `Accept-Language` through thirteen services would mean thirteen copies of one catalogue, which is exactly the divergence this codebase defends against everywhere else. Measured 03.09.2026: every answer on the wire is English, Girder's own refusals included.

**Tests run with the language pinned** (`locale: "de-DE"` in `playwright.config.ts`, `lng: "de"` in the component wrapper) and assert the German literals. That is deliberate: a test whose result depends on the machine's locale is green on one person's laptop and red on the next, and nobody sees why. Exactly **one** journey switches language and proves it works — `web/e2e/language-journey.spec.ts`, which also checks that the *mail* follows the choice while the browser still says `de-DE`.

**A cache that discards must take the in-flight request with it** (`features/work/lib/kontext.ts`). It remembers letter data across page switches, and until 10.09.2026 `vergiss`/`vergissKontext` cleared the maps while leaving a running fetch alone — which then wrote its result in *afterwards*, undoing the discard. Change your CV while an older fetch is in flight and the old one came back. The state is counted **per key** now; a fetch only writes if nobody discarded that key since it started.

It surfaced as a flaky test, and the first fix was wrong in an instructive way: `DraftPage` failed on the runner and passed five times locally, so the async wait went from 5 s to 10 s. **The next run failed identically.** It was never too slow, it was the wrong value — a stale answer from the previous test refilled the cache after `afterEach`. The wait is back at 5 s, and the lesson is the number itself: **raising a timeout always looks like a fix and just as often hides one.** `kontext.test.ts` now pins the race without depending on any timing — it decides when the late answer arrives — and the counter-probe drops exactly those three tests.

Playwright E2E lives in `web/e2e/` and runs against the **real** compose stack; there is deliberately no `webServer` in `playwright.config.ts`, because spinning up half an environment would abstract away exactly the integration these tests exist for. Without the stack they skip themselves — so check `curl localhost:<port>/health/live` yourself before trusting a green E2E run. `make validate-e2e` names the skips and prints how many journeys ran.

**They also run in CI now**, in a job of their own (`e2e`), which brings the whole stack up *including* `web` and fails if fewer than fifteen journeys ran. **The stack jobs configure the GitHub sign-in with invented values**, and that is not a workaround: `.env.example` leaves the three empty — correctly, because empty means *not set up*, the gist stays and the button never appears. Two checks are written against a stack where it *is* set up: `consents-journey` expects the button, and the route map expects `422` on `/github/me/oauth/finish`, which exists only *because* `start` created a row. Unconfigured, `start` creates nothing and `finish` answers `404`. **Both answers are right; they differ by configuration** — and the divergence was invisible until CI ran, because the developer's own `.env` had the credentials in it. Invented values are safe here because nothing goes out: `start` assembles the address in-process, and in `finish` the state comparison stands *before* the code exchange, so a request with a foreign `state` falls to 422 without ever asking github.com. They were the one gate no automation touched, and they are the only thing that exercises the wire between browser and service: a field sent in camelCase where the service reads snake_case never arrives, and every other suite stays green because none of them crosses that seam.

## The rules that carry the design

These are the parts that are easy to undo by "cleaning up". Each has a reason, and the reason is the point.

### Consent is read synchronously and never cached (ADR-0013)

`consent-service` is the enabler everything else leans on: `POST /consent/{grant,revoke,check,check-batch}`, `GET /consent/me`, `GET /consent/me/history`. A withdrawal has to take effect on the very next read, so a cache here is not a performance detail but a rule violation.

**`/check-batch` changes nothing about that** (ADR-0030): same synchronous read, no cache, no reason field — one round trip instead of forty. It exists because a candidate page measured 1.7–8.8 s and hung the UI; afterwards 0.015–0.083 s with byte-identical results. Answers come back **in the order of the questions**, and a caller refuses a mismatched length rather than guessing — misaligning them would show the wrong person's profile.

`/check` deliberately answers **without** the withdrawal reason: the reason is free text a person wrote about themselves and must not ride along on a query any authenticated caller may issue. `GET /consent/me` is the opposite case and takes **no** subject id at all, in path or query — a foreign list would say which *other* companies hold access.

**There is no `POST /consent/delete`** (ADR-0027 §1). It was *capability*-scoped, every capability in this system is a *visibility*, and "delete `profile.visibility:public`" therefore could never mean "delete the profile" — equating the two would erase a CV on a visibility withdrawal nobody asked for. The DELETE action stays and has exactly one producer: account erasure.

### Visibility lives in the ledger, nowhere else (ADR-0020)

`profile-service` has **no visibility field**. Its status codes carry the design and are easy to break by tidying:

- **404** means *hidden or non-existent*, and the two must stay byte-identical apart from the correlation id.
- **403** means *no active company* — a statement about the caller, which leaks nothing.
- **503** means *the ledger is silent* — neither 404 nor showing the profile, because both would assert something nobody knows.

`resume-service` is the same idea one step stricter. A profile is a notice board; a CV names real employers with dates — exactly what a current employer must not see. So there is **no public switch**: a company asks, the person answers, and the release covers that one company. The load-bearing distinction is that **the request is not the permission**: granted means "was granted once", not "holds now", so a resume request has neither an active flag nor a revoked timestamp. After a withdrawal the request stays granted and the read still comes up empty — that is the design, not a bug to fix. Asking requires the *profile* release, never the existence of a CV: "has already written one" is a fact about the person nobody should be able to probe for.

Certificates ride a capability of their own: `documents.visibility:tenant:<id>` (ADR-0035). They are not folded under `resume` — a career history is self-written text, a certificate is a third-party document with names and grades, and whoever wants to show one without the other must be able to.

### Erasure: the default deletes completely (ADR-0027)

`POST /account/erasure` at identity-service — self only, **no reason field**: demanding a justification from someone who wants to leave is a lever against them. It immediately revokes every session and disables the account, then cascades over the **outbox** to nine recipients (`consent`, `profile`, `resume`, `portfolio`, `applications`, `transfer`, `github`, `notification`, `scout`) and identity itself, last. `jobs-service` and `companies-service` are **not** recipients — they hold nothing about a natural person, and `LoeschempfaengerTests` goes red the moment any service grows a personal table and is not on the list. That guard reads the **EF model**, not the source, and uses two signals: personal column names *and* the `Personenzeile` annotation.

Three things are load-bearing:

- **The default deletes everything — including hired applications and paid transfers.** The retention exception is one named constant per affected service (`Aufbewahrung.EingestellteBehalten`, `Aufbewahrung.BezahlteBehalten`), it is `false`, and it is a **constant, not a setting**: with a deletion promise, "different in production than in test" is the worst possible state. It is also `static readonly` rather than `const`, because a `const` is inlined into the calling assembly and a reverted counter-probe then goes unnoticed. Tests pin the value *and* pin that the flipped switch covers exactly two row classes. Do not widen it.
- **The erasure delivery must be able to fail.** It raises on transport error *and* on non-2xx, where the ordinary notifier swallows both — right for a mail, and here it would make the delivery timestamp a lie. Erasure runs with **no attempt ceiling** and growing backoff. A permanently dead recipient therefore *blocks* completion — that is the point, and no timeout may "finish" it.
- **The order is enforced, not advised:** every acknowledgement → final mail → only then the user row falls. `NochNichtException` expresses "not yet" without spending an attempt. The final notice says only *that* it is done; listing what was deleted would copy the data into an inbox that may not be the person's alone.

`/delete-account` is where the promise is made to a person: it states **before** the button what disappears, that it is **not immediate**, and the uncomfortable part out loud — *the application you were hired through disappears from the company's list too*. It promises no exception, because in the default there is none. Confirmation is two deliberate steps with a differently worded second button — no typed word, no cooling-off period, no re-entered password: whoever wants to delete may. Afterwards it says "läuft", never "erledigt", and shows no progress bar. One trap: the page clears the session on success, so the shell re-renders it with no principal — the accepted state must take precedence over the login prompt, or the person sees "Bitte anmelden" right after deleting their account.

The ledger survives as the **proof**: the grant/revoke chain stays, one DELETE row per capability ever held is appended, and reasons and metadata are cleared. The argument is not "we may retain" but that nothing maps subject id → person afterwards.

### The skill vocabulary renames, it never infers (ADR-0023)

`src/shared/WorkerTransfer.Skills` is a table plus a function, applied *inside* the skill value objects of `profile-service` and `jobs-service` — not in an endpoint, so it also covers rows written before it existed. Order is load-bearing: canonicalise first, deduplicate second.

`"Postgres" == "PostgreSQL"` is a statement about *language* and is allowed. `"React implies JavaScript"` is a statement about a *person* and is forbidden — it credits someone with a skill they did not claim, where they cannot object. No level, no weight, no kinship, no likelihood-to-switch. It never rejects and never invents: what it does not know stays exactly as typed, because a list of permitted skills would be a claim about which work exists. A test fails if the module ever exposes a name containing `level`/`weight`/`score`/`rank`/`implies`.

### Fit is computed in the browser and exists nowhere else

Job skills and profile skills are compared in `web/src/features/work/lib/match.ts`; the result is a checklist ("Du hast 2 von 3 genannten Fähigkeiten: Python ✓ · Kubernetes ✓ · Go ✗") shown **to the person, never to the company**, and it ranks *jobs*, not people.

There is deliberately no score, no percentage, no match endpoint and no match column. A ranked candidate list with a percentage is exactly what ADR-0022 forbids, arriving through the back door — and a percentage hides the only thing that helps: *which* skill is missing. Do not add a server-side match, a sort by fit, or a "0 von 3" for someone who entered nothing: they said nothing, which is not the same as knowing nothing. Job skills use the *same* limits as profile skills, and a test pins that a job may never demand a skill longer than a profile can hold — such a line could never become a tick.

### The AI seam drafts on request and stores nothing (ADR-0024)

There are **two** consumers and they are mirror images. `POST /profiles/me/draft` helps a *person* say what they want to say; `POST /jobs/draft` helps a *company* phrase **its own advert**. Neither ever says anything *about* anyone — that is what makes the second one buildable without its own weighing, where a scout, a candidate ranking or a salary recommendation would all aim at people.

Each service owns its own `IEntwerfer` port and its own context type rather than sharing one prompt with branches — the profile context carries no name, no email, no subject id, no employer, no CV; the job context carries no tenant and no company name. Tests pin both field sets. A shared prompt with an `if` is exactly where "invent nothing about the person" would one day apply to an advert, or worse, the reverse.

With no API key configured, nothing external is ever called and the UI says so. Nothing is stored — not the prompt, not the answer; the draft lives in the form until it is saved, and then it is the author's text. Errors report the failure kind, never the content. Do not add memory, a vector store, or a background suggestion — all three either store or act unasked. **There is deliberately no plan-act-reflect loop and no ledger entry**: a reflect stage means two calls to rewrite the user's own words unasked, and a ledger entry would assert a standing permission nobody gave — the button is the consent, informed and per use.

### What ADR-0022 actually forbids

**Read this before building anything that looks at a person.** ADR-0022 is routinely read as a ban on the whole subject. It is not, and reading it that way has already cost one wrong decision.

The ADR deleted a package that computed a person into **one number between 0 and 100** from ten dimensions with fixed weights, and that measured skill in a language as a share of written bytes. It forbids **exactly three things**, and all three still hold:

1. **A number that summarises a person — and any ranking derived from it.** The reason survives the formula: *"nobody justified the weights. They are an opinion in the costume of a formula."* And a decimal place looks like a measurement.
2. **Derived attributes without a basis** — "leadership", "community", "architecture" out of repository metadata; or skill as `bytes / total_bytes`, where a checked-in dependency beats a careful library and whoever writes little and well loses.
3. **Silent completeness.** *"Whoever has nothing on GitHub is not worse, but elsewhere. A view that does not say so lies by omission."*

The same ADR has a section called *"what may come back in Phase 6"*, and it is explicit:

- **Evidence with provenance** — "made *these* commits on *this* project", checkable, with a link, **without an intermediate calculation**.
- **Consent first.** A person connects their own account; the platform looks at nothing and reads about nobody who was not asked (ADR-0004: no scraping).
- **Visibility through the ledger**, like everything else — a capability, revocable at any time, effective immediately (ADR-0013).

So the line is not "no analysis". It is the **direction of the question**: requirement in, evidence out — never person in, number out. A tick says *which* skill is missing and can be contradicted; a number hides exactly that. `github-service` exists and was built *under* this ADR, not condemned by it — its domain module says so in its first paragraph, and `Adr0022Tests` scans its domain and contract assemblies for the forbidden vocabulary.

### scout-service: ticks and evidence, never a number

Built 11.09.2026 under ADR-0036. It replaced `GET /candidates` in
profile-service, and **`/candidates` is gone** — endpoint, gateway route, client
and the UI, all in the same step. `docs/routenkarte.yml` keeps the old path as a
**dead door** (404 in all four columns) and `LandkarteTests` measures that no
route stands behind it any more. Two searches side by side would have been two
truths.

Three things fell with it, because they had no caller left: the AND branch in
the profile store, the batch question in profile-service's consent gate, and the
per-card GitHub fetch in the browser. A branch without a caller is what later
gets revived wrongly.

The hard parts of `/candidates` were **carried over, not reinvented**: the
company requirement (a person acting for themselves gets 403), the ledger asked
**per row over `/check-batch`** (ADR-0030, synchronous, no cache), **no total
count** (ADR-0026 — the difference to the page length would say how many
profiles are *not* released), no filling up a short page, and the stable order
`updated_at DESC, id DESC`.

**Four conditions, each a test** (`AuflagenTests`), and each with a counter-probe
that was measured to fall:

1. **No ordering by fit.** Two identical searches give the same order, and
   whoever matches more of the searched words does *not* move up. There is
   nothing to sort by either: a rank out of a tick list needs an invented number
   first.
2. **No number about a person.** A twin of `Adr0022Tests` over Domain and
   Contracts, forbidding `fit`, `score`, `rank`, `percent`, `passung`, `anzahl`
   and their siblings — which is why the page size in this service is called
   `Seitenlaenge`.
3. **Only what was *named* is searchable.** Evidence (GitHub topics, language
   names) is **fetched to the hit, never used to find it**: a topic is a
   statement about an *artefact*, and searching by it would quietly make it one
   about the person (ADR-0033). `Suchfilter` has no field for one, and evidence
   is fetched only *after* the ledger has answered — so nothing is looked up
   about someone who released nothing.
4. **The approach is a draft.** The service has no postman, no address and no
   send path; `AuflagenTests` scans the application assembly for one. The text
   goes to the browser of whoever asked and lies in a form.

**The search is an OR, and that is what makes the tick list an answer.**
`/candidates` searched with AND — every hit then satisfies every condition,
every tick would be set, and "which skill is missing" would have no answer;
condition 1 would be empty too, since there would be nothing to sort. The
internal door in profile-service (`GET /internal/profiles/search`, behind the
shared secret, no gateway route) therefore searches with OR, and it is the only
mode left.

**That door deliberately does not ask the ledger.** The check stands one line
higher, in scout-service, for the whole page at once: exactly *one* place decides
about visibility, and two would be two truths. Whoever uses that door for
something else fetches the check along with it.

**Stored is the request, never the result** (`searches`): filters and a name. A
stored result about people goes stale against a withdrawal, and a withdrawal has
to take effect on the next call (ADR-0013). For the same reason there is no
nightly run that searches and mails "new hits" to the company — that would be the
same cache with an alarm clock in front of it.

**"Your profile was discovered"** is its own notification kind
(`profile_discovered`, ADR-0033). It names **no company**, travels over the
content-free outbox (ADR-0025), and is capped at **one per person per day** —
and that cap takes the *inbox entry* with it, not just the mail. One entry per
hit would be a counter over one's own visibility, and on a detour exactly the
register of who looked at whom that this design refuses to build. The cap lives
in notification-service and is answered from the person's own inbox, so no new
row is needed anywhere; delivery stays at-least-once and a second delivery is
harmless.

There is no `/me/scouting` and no table of who looked at whom.

**Three filters PBI-3 names are decided against, not pending** (ADR-0036 §6–8):
the **occupational field** never becomes a filter — it contradicts ADR-0039
("no visibility follows from it") and would find *less*, since the vocabulary
already carries MIG/WIG/CNC/SPS; **availability** is not filtered, because the
market status has its own release and filtering by it would reveal
approachability without asking; and the **radius** comes as
[ADR-0041](docs/adr/0041-entfernung-ist-eine-frage-zwischen-zwei-aussagen.md) —
a person states how far they will commute (10/25/50/100/egal), an advert states
its attendance (remote/hybrid/vor_ort), and the two make a **tick**, never a
kilometre figure and never a filter that removes somebody. Nothing new is
stored: `Ortskunde` resolves the existing free-text location at search time.
That ADR is written and **not yet built**.

### Planned, not built: advisor and assessment

[`docs/SCOUT-UND-BERATER.md`](docs/SCOUT-UND-BERATER.md) is a **draft**. The inventory in [`docs/SCOUT-UND-BERATER-BESTAND.md`](docs/SCOUT-UND-BERATER-BESTAND.md) measured what already exists and **refutes parts of that draft**. ADR-0034 is the cover-letter agent, ADR-0035 the application folder.

**`scout-service` is built** (ADR-0036, 11.09.2026) — see its own section below. ADR-0037 (`advisor-service`) is a decision and is **not** built; assessment has no ADR at all. **No agent builds advisor or assessment** without being asked for it by name.

When an application arrives, company **members** get a mail of kind `application_received`. A company has no mailbox. The outbox stays content-free (ADR-0025): id and kind, never a name.

## Conventions that bite

- **Package managers are `dotnet`/NuGet and `pnpm`.** Never `npm`, never `yarn`. There is no Python here any more; if a command in an old document says `uv`, `ruff`, `mypy`, `pytest` or `alembic`, that document is describing the predecessor.
- **Warnings are errors** (`Directory.Build.props`). Central package management via `Directory.Packages.props` — versions go there, not into a `.csproj`.
- **Sharing rule**: only domain-neutral, transport-independent, non-business code goes in `src/shared/`. Profile, company, job, transfer, contract, application and matching models stay inside the owning service. `Contracts.*` holds versioned boundary DTOs, never a shared domain model.
- **A service-to-service body is a typed contract, never an anonymous object.** `new { userId = … }` compiles, serialises, and arrives as `Guid.Empty` at a receiver that declares `[JsonPropertyName("user_id")]`. Measured: all **four** notification hops did exactly this, and the whole notification path had never once delivered — 18 outbox rows given up after ten attempts, and the mailbox held nothing but confirmation mails. The erasure cascade was immune because sender and receiver share `LoeschungV1`. Where a shared type is genuinely not available, `BenachrichtigungsdrahtTests` is the pattern: capture what the sender writes, deserialise it with the *receiver's* type, and assert the value survives — never compare the two names, which is one string checked twice.
- **No secrets, tokens, CVs, contracts or raw source code in the repo or in logs.**
- **No detour around a Girder bug.** Write the code correctly, leave the test red, file a ticket in `bugs/` with a reproduction free of WorkerTransfer code, move on. Everything in `bugs/` must go green once the bug is fixed, without anyone reverting code.
- **Run counter-probes, and make sure the break actually compiles** — a build error reads in the output like a passing test.
- **After a counter-probe, build with `--no-incremental`** — and after the *revert*, not only after the patch. The incremental build has failed to notice a revert three times: twice a test fell over code that was already correct, and once a counter-probe *passed* while the rule was genuinely missing. The second case is the dangerous one, because it looks like a licence.
- **A counter-probe that does not fall reveals a weak test, not correct code.** This has happened for the URL scheme check, "validate before write", the unverified-connection guard, the fork filter, the route priorities and the database-creation guard. Every time the answer was to strengthen the test, not to drop the claim.
- **Docker must be running**, or the integration suites skip themselves — and a skipped integration test looks exactly like a passing one. `scripts/test-dotnet.sh` prints the counts for this reason.
- **Never build images and run tests at the same time.** They contend, and the failure looks like a test failure.
- New cross-cutting architectural decisions get an ADR in `docs/adr/`.

## Key docs

- `docs/MIGRATION-STAND.md` — what was decided and, more usefully, what was *measured*.
- `docs/architecture.md` — service shape, sharing rule, delivery sequence.
- `docs/product-scope.md` — consent, AI, data-acquisition and security constraints. Read before touching anything consent- or AI-related.
- `docs/adr/` — the architecture decision records. They predate the migration and still govern.
- `docs/SCOUT-UND-BERATER.md` — planned, not built. See above.
- `docs/glossary.md` — the terms.
- `bugs/` — Girder debts, each with a reproduction. All currently closed: the correlation id reaches stdout since 4.3.0, measured across a real service hop on 09.09.2026.
- `AGENTS.md` — concise command + convention reference.
