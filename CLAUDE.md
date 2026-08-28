# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

WorkerTransfer is a consent-first talent-mobility platform (applications, direct recruiting, employment transfers, AI-assisted career workflows). It is **.NET 10 on [Girder](https://github.com/DavidOeztuerk) 3.0.1** — a shared foundation library of this author's, pulled from GitHub Packages — plus a React app.

Eleven services and a gateway live under `src/`, their tests under `tests/`, the React app in `apps/web` with its component library in `packages/ui`.

The repository was a Python (`uv`) monorepo until August 2026 and was translated by hand, service by service. **Everything Python is gone**: no `pyproject.toml`, no `uv.lock`, no `apps/<service>`, no `packages/worker-*`, no alembic, ruff, mypy or pytest. `docs/MIGRATION-STAND.md` records what was decided along the way and, more usefully, what was *measured* — read it before assuming a shape is arbitrary. The ADRs in [`docs/adr/`](docs/adr/) predate the migration and **still govern**: they hold the reasons, and reasons do not change language. Where an ADR names a Python path, read it as naming the decision, not the file.

### The layout, so nobody guesses

```
src/            eleven services, gateway/, shared/
tests/          fifteen test projects
apps/web/       the React app — deliberately still here
packages/ui/    its component library — deliberately still here
docs/  bugs/  deploy/  docker/  scripts/  .github/
WorkerTransfer.slnx  Directory.Build.props  Directory.Packages.props  NuGet.Config
package.json  pnpm-lock.yaml  pnpm-workspace.yaml  turbo.json  tsconfig.base.json
```

`apps/web` and `packages/ui` are **not** under `src/` and are not moving: the pnpm workspace points at those paths, they are frontend rather than leftovers, and relocating them would cost a large diff for nothing.

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

The gateway (`src/gateway`, port 8090) is the single entrance. `src/shared/` holds six things and no more: `ServiceDefaults` (the one call every service makes), `Outbox`, `Skills`, and three `Contracts.*` packages (`Identity`, `Consent`, `Erasure`) that carry versioned boundary DTOs — never a shared domain model.

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
make routenkarte    # every endpoint × three principals, against the running stack
make k8s-up / k8s-down / k8s-lint
```

**Build and test in separate invocations.** Chained (`build && test`), the Testcontainers suites fail *and look like real test failures*. This has cost time more than once.

**Never `dotnet test` over the solution.** Fifteen suites start fifteen Postgres containers at once, Testcontainers' ResourceReaper times out, and *all* suites fail within a millisecond with `TypeInitializationException` — which looks exactly like a broken build and is not one. `scripts/test-dotnet.sh` runs them one at a time, runs through instead of failing fast, prints how many tests actually ran, and goes red if a suite executed nothing. A green run that silently collected half the suite looks identical to a real one unless the count is on screen.

Single suite / single test:

```bash
dotnet test tests/WorkerTransfer.Identity.Tests/WorkerTransfer.Identity.Tests.csproj --no-build
dotnet test tests/WorkerTransfer.Consent.Tests/... --no-build --filter "FullyQualifiedName~Widerruf"
```

Restoring needs a NuGet login — Girder is in GitHub Packages:

```bash
dotnet nuget add source https://nuget.pkg.github.com/DavidOeztuerk/index.json \
  --name GitHub --username <you> --password <token> --store-password-in-clear-text
```

`NuGet.Config` also pins package source mapping: only `Girder.*` may come from GitHub, everything else from nuget.org. Without that, a public package of the same name could answer first.

### Configuration comes from the environment

`make env` is the first command in a fresh clone. It copies `.env.example` — which is complete, one comment per key — and **rolls the three secrets** with `openssl rand -base64 32`. `.env` is git-ignored; `.env.example` ships with the secrets **empty**.

That emptiness is the point. A built-in default *is* the secret, and it then lives in git. `docker-compose.yml` therefore uses `${WORKERTRANSFER_JWT_SECRET:?…}`, not `${…:-dev-only-secret}`: without a value, compose aborts and **names the missing variable**. Girder does the same at its own most important place — `JWT_SECRET` beats `JwtSettings:Secret`, and if both are absent it throws a `ConfigurationException` naming the key.

`Umgebung.Laden()` (`ServiceDefaults`) is the **first line of every one of the twelve `Program.cs`**, before `CreateBuilder` — the configuration builder reads environment variables exactly once, when it builds, so loading afterwards means loading and nobody reading. A test pins that order in all twelve. It never overwrites an already-set variable: in compose and in the cluster the environment comes from there, and a file left in the image must never override it.

Infisical will later fill the environment. It feeds `.env`; it does not replace the mechanism, so no code changes for it.

### The stack

`docker compose up` is the whole local environment; there is no companion script. Each service migrates its own schema on start (`ServiceDefaults.Wanderung`), so a fresh clone needs no manual step. Source is not bind-mounted — a code change needs `docker compose up -d --build <service>`.

**One image serves all twelve entry points** (ADR-0028). They differ only in `SERVICE_DIR`, which `docker/dotnet-entrypoint.sh` reads from the *environment*, not from a build arg — so the image is built once, without the arg, and the container says who it is. The entrypoint finds the entry assembly as the only `*.runtimeconfig.json` under `/app/$SERVICE_DIR` (no name table to keep in sync) and **`cd`s into that directory** before starting: ASP.NET's content root is the working directory, and from `/app` no service reads its own `appsettings.json`. Measured at the first `compose up`, where only the gateway died visibly, on its missing `ocelot.json`.

Adding a service is three steps and no new Dockerfile: its database in `scripts/initdb/`, its entry point in `docker/dotnet-service.Dockerfile`, a copied block in `docker-compose.yml` with three values changed — plus its route in `src/gateway/WorkerTransfer.Gateway/ocelot.json`.

**`curl` is in the runtime image on purpose.** Docker's healthcheck asks from *inside* the container, and the aspnet runtime image ships no curl, no wget, no nc; `sh` is dash and cannot do `/dev/tcp`. The probe used to be `dotnet --version`, which can *never* succeed on a runtime image (no SDK, exit 155) — every service reported `unhealthy` for months while answering perfectly, and a probe that is always red is worse than none. One probe now lives in the `x-dienst` anchor, covers all eleven services *and* the gateway, asks `/health/live`, and derives the port from `ASPNETCORE_URLS` so no second list of ports can drift. Kubernetes does not need any of this — there the kubelet asks over HTTP from outside.

### Kubernetes — a staging environment on your own machine

```bash
make k8s-up      # kind cluster + both images + helm release + PROOF
make k8s-down    # delete the cluster and its data
make k8s-lint    # helm lint + render, no cluster needed
```

The app answers on **`http://localhost:8090`** — not 8080, which belongs to the compose gateway; two environments fighting over one port produce exactly the debugging session where nobody can say who answered. Mailpit is on `:8025`.

Load-bearing, and easy to undo by tidying up (ADR-0028):

- **The route map stays one file.** The k8s `Service` objects are named exactly like the compose services, so `ocelot.json` travels in the image and the same map serves both. Re-expressing those non-disjoint paths and their priorities as Ingress rules would duplicate the map, and the copy would be wrong at the first new path.
- **`replicaCount: 1` is a decision, not a starting value.** For the eleven services one reason remains: **every service migrates its schema at startup**, so two pods are two pilgrims on one schema. (The outbox dispatcher's missing `SKIP LOCKED` is fixed.) The **gateway** is pinned separately, and for its own reason: the auth brake counts in-process — see below.
- **The web image is a built artifact, and that forces a runtime config.** `vite build` bakes `import.meta.env.VITE_*` into the bundle, so an image with baked URLs cannot be the same in two environments. `apps/web/src/env.ts` resolves in three steps — `window.__WT_CONFIG__` → `VITE_*` → port fallback — and `apps/web/public/config.js` is an empty object that changes nothing locally. Only the chart lays a real one over it. Do not "simplify" that back to a build arg.

**`make k8s-up` has never been run on this machine.** The chart lints and renders; only a run proves it works.

### The brake on the auth endpoints, and why it lives in the gateway

Five paths are braked — `/auth/login` (20/min), `/auth/register` (5), `/auth/resend-verification` (3), `/auth/verify-email` (20), `/auth/refresh` (60) — per origin, per minute. The rules live in `ocelot.json` next to the routes they select from, and `Bremse.cs` is the middleware.

**Per origin, never per email address.** Keying on the address would build exactly the enumeration channel `/auth/register` closes: a braked answer would confirm the address exists. It would also let a stranger lock out anyone whose address they know. The key is path plus origin, and the brake never reads the body — a test sends five *different* addresses from one origin and requires them to share one bucket.

**In the gateway, and that was measured rather than chosen.** A brake needs the caller's origin, which lives in `Connection.RemoteIpAddress`. Behind the gateway that is the *gateway's* address, identical for everyone — a brake inside identity-service would have thrown all people into one bucket, so the first person to mistype their password locks out the world. The usual escape, `X-Forwarded-For`, is worse than the problem here: any caller sets that header themselves, so trusting it hands the attacker the key to the counter. It is therefore not read, and a test pins that a forged one changes nothing.

It sits **outside authentication** in the strongest sense available: no token has been verified and no password hashed when it runs. And it sits **after** the health probes — a braked liveness probe would take the container out of the load balancer, making the brake itself the outage.

**The counter runs in-process**, so the gateway is pinned to one replica. The way out is a registration change, not a rewrite: `RedisDistributedRateLimitStore` satisfies the same interface.

**None of Girder's three rate limiters is used, and that is measured rather than assumed.** `DistributedRateLimitingMiddleware` and `RateLimitMiddleware` are wired but do not brake; `RateLimitingMiddleware` brakes but has no caller anywhere in Girder. The reason that would survive a fix: **all three trust `X-Forwarded-For` and `X-Real-IP` unconditionally**, and no trusted-proxy list exists anywhere in the library — eight requests against a limit of three all passed, simply by rotating a header the caller sets. Girder's *counter* (`IDistributedRateLimitStore`) is sound and is exactly what we do use. Full write-up in `bugs/ratenbegrenzung-drei-wege-zwei-bremsen-nicht.md`.

### CI

`.github/workflows/ci.yml` has four jobs: `backend-quality` (restore → build → `scripts/test-dotnet.sh`, in separate steps), `frontend-quality` (check, test, **build**), `dependency-audit` (`dotnet list package --vulnerable` + `pnpm audit --prod`), and `images`.

`images` is the one that used to be missing, and its absence is why a Dependabot bump to `node:25` passed CI while breaking the web image. It builds both shipped images, brings the whole compose stack up with `--wait`, then asks twice through the gateway: `GET /jobs` (401 plus an RFC 9457 document with `correlationId` — a job list sits behind login, and the proof of routing is *whose* answer it is, not that it succeeded) and `POST /auth/register` (201). Only the write proves the migrations ran; the read answers on an empty database too.

`--wait` is deliberate. An earlier draft started each service and checked `.State.Running` after three seconds; the counter-probe (take away the gateway's `ocelot.json`) showed a service that throws in `Program.Main` and *keeps running* at 99% CPU, still `Running=true` after 45 seconds. The check was green over a process producing nothing but a stack trace.

`dependency-audit` sets `DOTNET_CLI_UI_LANGUAGE: en` because it *reads* the output, and the same command answers German locally and English on the runner — a grep for one of the two sentences would be silently always-true in the other environment. It also fails if the scan named fewer than 50 projects: a scan that examined nothing is otherwise indistinguishable from a clean result. Neither audit runs with `continue-on-error`; a scan that is allowed to be red gets ignored after the second week, and is then worse than none.

**There is no `dependabot.yml`** (deleted 08.08.2026). This job is the only thing that asks.

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

`ServiceDefaults.AddWorkerTransferDefaults()` is the one call every service makes. What is in it is there because eleven services answering it eleven ways would be eleven chances to answer it wrong: how a token is verified, what a failure looks like on the wire, the order of the pipeline. What is *not* in it is everything that is a decision — which database, which repositories, whether there is a cache, whether there is an outbox. Those stay in each service's own composition root, where a reader can see them.

### CQRS

Mediator via Girder's `AddCQRS`, but with **our own `IBefehl` / `IAbfrage` markers** over MediatR's `IRequest`, defined per service in `Application/Nachrichten/`. This is deliberate: Girder's `ICommand<T>` carries its own error envelope, which would be a second one beside RFC 9457, and two shapes for "what went wrong" is exactly the divergence that later gets papered over in the UI. `TransaktionsBehavior` wraps **commands only** — a query has nothing to commit.

### Errors

RFC 9457 problem documents, everywhere, from `ServiceDefaults.ProblemDetailsMiddleware`. Every response carries a `correlationId`. **No values in logs** — shapes, not contents. Girder logs no values either, neither redacted nor scrubbed; do not build that back.

An endpoint filter that has already written a response must return `Results.Empty`, never `null`: with headers sent, the framework writes a JSON null after them, which tears the connection on a POST-with-body and is invisible on a GET except in the log.

### Request context

Correlation and tenant flow through the request, set by middleware in `ServiceDefaults`. **Tenant identity must never come from a browser header in production.**

**A tenant is a company, and a natural person has none** (ADR-0017). This is modelled as `Capacity`: `AsSelf` or `ForCompany(TenantId)`. Tenant is an *optional* attribute of a principal, carried only by company-based features (adverts, employer accounts, recruiting teams). It is **not** the scoping axis for personal data: user data is scoped by subject identity, and both axes coexist. The consent ledger therefore has no tenant column by design — a consent belongs to the person and follows them across employers. Do not "fix" that by adding one.

Company membership is a relation, not a column on the user — one person may act for several companies (ADR-0018). Email is globally unique. `POST /auth/login` returns a person token with **no** tenant claim; `POST /auth/company/{id}` verifies membership and only then mints a token carrying the tenant. So the client names the company but the server decides, and the tenant in the token never came from client input. `null` means "acted as a person", not "missing".

The access token carries `sub`, `email`, `jti`, `iat`, `exp`, `iss`, `aud`, `session_id` and — only while acting for a company — `tenant`. **Nothing else.** During the migration it also carried `tenant_id` and `type`; both are gone, and `TokenformTests` now pins their *absence* in both capacities. Roles and permissions are not in the token: they are read from the membership table per operation, so the token was never authoritative.

`AuthMiddleware` resolves the principal from an `Authorization: Bearer` header **or** the `access` cookie, in that order. Both carriers are needed: service-to-service and CLI callers send the header; the browser never sees the `httpOnly` token and can only replay it as a cookie. Any new service verifying identity tokens must accept both.

**A company is created at registration and nowhere else.** `POST /register` takes an optional company name; the intent is stored on the pending user and redeemed by `POST /auth/verify-email`, in the same transaction. The order is forced, not chosen: creating a company requires an *active* account and derives the domain from the **confirmed** address, so it is proven before the company exists and cannot be forged (ADR-0019).

Two checks sit in two places on purpose. **Freemail is rejected at registration**, and **before** the existence check — otherwise a *known* freemail address would get the silent "ok" and an unknown one a 422, and that difference is exactly the enumeration channel `/auth/register` closes. **"Domain already claimed" is checked only at confirmation**: earlier it would answer *"is firma.de on this platform?"* for anyone who guesses a domain. That creates a state which did not exist before — **account confirmed, company refused** — and `/verify` says so; the confirmation itself must never fail because a name was taken.

**There is no "create a company" button and no `/company/new` route.** Someone already registered as a person joins a company by **invitation**, which is also the right answer when their domain is already claimed — they have colleagues there. `admin` vs `member` is **not enforced anywhere** yet, and no route checks the tenant: the header only *hides* company entries; the server answers 403. Do not mistake the navigation for access control.

**Registration is open to any email address, private ones very much included** — the transfer market's normal user is a person with no company. The freemail blocklist applies at exactly one place: claiming a domain. Accounts start pending and are activated by a mailed token; register and resend answer identically whether or not the address is known, because a differing answer would reveal platform membership without asking the ledger.

### Persistence

EF Core, one `DbContext` per service, one database per service — **no shared database and no cross-service repository abstraction** (ADR-0004). Migrations live under `src/<service>/…Infrastructure/Persistence/Migrations` and are applied at startup by `ServiceDefaults.Wanderung`, which retries **only transient failures** for one minute. That narrowness matters: an earlier version caught everything, and a schema error became a minute of retries ending in a misleading `type … already exists`.

**Aggregates come back detached from the repositories.** A mutation reaches the database only via an explicit save. Forgetting it costs nothing at test time and silently loses the write in production.

`Personenzeile` is an EF annotation put on tables whose *key* is the person (`profiles`, `portfolios`, `github_connections`, `resumes`, `notification_preferences`, `market_status`). It exists so the erasure guard can recognise them: those tables have no `subject_id` column, because the id *is* the subject, and a guard looking only for column names would miss exactly the tables that hold the most.

### The outbox

`WorkerTransfer.Outbox` records an *intent* in the **same transaction** as the domain change; a dispatcher delivers it with retries (ADR-0025). The promise it keeps is the old one — a failed mail must never topple the transaction — kept differently.

The table deliberately holds **no content**, only a user id and a kind. An outbox is durable storage and ends up in every backup, so a payload column would be an invitation to write message text into it. **Never carry an email address into it.** Giving up means leaving the row, never deleting it. Delivery is **at-least-once**. There is no broker, and none is planned.

### The Girder modules, all eighteen

Girder exposes **18** module entry points on `InfrastructureBuilder`. We call **7**. This table is the standing answer to "why not that one?", and the rule behind it is inverted from what it used to be: **a module is called unless there is a measured reason against it.**

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
| `AddInputSanitization` | **offen — und der Auftrag irrte** | Registriert `IInputSanitizer`, `IInputValidator`, `IInjectionDetector`: XSS- und Injektionsabwehr am HTTP-Rand. Es hat **nichts** mit der fehlenden Eingabeprüfung in der CQRS-Pipeline zu tun (nächste Zeile). Seine Middleware muss zusätzlich von Hand eingehängt werden; `AddInputSanitizationMiddleware()` ist eine leere Methode. |
| *(Validierung, kein Modul)* | **läuft schon, hat aber nichts zu tun** | `AddCQRS` hängt `ValidationBehavior` **bereits** in jede Pipeline und ruft **bereits** `AddValidatorsFromAssemblies`. Das Verhalten steigt sofort aus, wenn es keine Validatoren findet — und wir haben **null** `AbstractValidator` geschrieben. Es fehlt also keine Verdrahtung, sondern der Inhalt. Achtung beim Schreiben: `ValidationBehavior` **protokolliert die Fehlermeldungen**, eine Meldung muss deshalb die Regel nennen und nie den Wert. |
| `AddResilience` | **bewusst nicht** | Registriert nur `ICircuitBreakerFactory` und `IRetryPolicyFactory` — es umhüllt **keinen** HttpClient. Wirksam würde es über `AddResilientHttpClient<T>`, und dessen Handler macht aus **jedem** Nicht-2xx eine Ausnahme und wiederholt sie: ein `404` würde dreimal nachgefragt und käme als Ausnahme an. Bei uns ist ein Statuscode eine Antwort (`bugs/statuscodes-werden-als-stoerung-behandelt.md`). Das echte Risiko lag woanders und ist behoben: **alle fünfzehn Aufrufstellen setzen ein eigenes Zeitlimit** — sieben taten es nicht und liefen in die 100-Sekunden-Vorgabe von `HttpClient`. `ZeitlimitTests` hält das fest. |
| `AddCommunication` | **bewusst nicht, vorerst** | `ServiceCommunicationManager` ist das **einzige** Bauteil in Girder, das die Korrelationskennung weiterreicht (Zeile 386) — und ohne Broker registrierbar: `AddServiceCommunication(configuration)` verlangt weder `IEventBus` noch `IDistributedCache`, die Forderungen stehen nur im Modul. Der Umstieg scheitert trotzdem: `GetAsync`/`SendRequestAsync` machen aus **jedem** Nicht-2xx ein `null`. Damit wäre für die Löschkaskade ein `500` von einem Empfänger nicht von einer leeren Erfolgsantwort zu unterscheiden — `delivered_at` würde zur Lüge (ADR-0027) — und `profile-service` könnte `404` nicht mehr von `503` trennen. Ticket geschrieben; bis dahin bleibt unsere Korrelationskette am ersten Sprung offen. |
| `AddDistributedRateLimiting` | **bewusst nicht** | Eine von **drei** Ratenbegrenzungen, und die gemessen kaputte. Alle drei glauben `X-Forwarded-For` bedingungslos, eine Vertrauensliste gibt es in Girder nirgends. Siehe `bugs/ratenbegrenzung-drei-wege-zwei-bremsen-nicht.md`. |
| `AddCaching` | **bewusst nicht** | HTTP-Antwort-Caching plus `CacheInvalidationService`, und es verlangt einen `IDistributedCacheService`. Eine Einwilligung muss sofort wirken (das gilt, weil es richtig ist, nicht weil ein ADR es sagt) — was zwischengespeichert wird, muss deshalb einzeln entschieden werden, nicht global eingeschaltet. |
| `AddAuditLogging` | **bewusst nicht** | Registriert `ISecurityAuditLogger`, der ins **Protokoll** schreibt; `AuditBehavior` in der Pipeline wirkt auf `IAuditableCommand`, das wir nicht umsetzen. Unsere Prüfspur ist eine **Tabelle** (`EfPruefspur`) in derselben Transaktion wie die Änderung: sie ist Beleg, Girders ist Telemetrie. Beides kann nebeneinander stehen — nur ersetzt keins das andere. |
| `AddEncryption` | **bewusst nicht** | Verlangt `IDataEncryptionService` **und** `IMasterKeyProvider` — beide kommen aus `Girder.Redis` oder einem Geheimnisspeicher. Wir verschlüsseln heute auf Feldebene nichts; wer damit anfängt, entscheidet zuerst, wo der Hauptschlüssel liegt, und das ist H2. |
| `AddSecretManagement` | **offen, gehört zu H2** | Geheimnisverwaltung samt Rotation. Genau die Frage, die H2 stellt — erst `.env` und die Rangfolge, dann entscheiden, ob dieses Modul den Platz von Infisical einnimmt oder daneben steht. |
| `AddSecurityMonitoring` | **bewusst nicht** | Alarme und Bedrohungssignale, verlangt einen `IDistributedCache`. Ein Alarmweg ohne Empfänger ist ein Protokolleintrag mehr; das lohnt erst, wenn jemand ihn liest. |
| `AddResourceAuthorization` | **offen** | Ressourcen- und Eigentümerprüfungen. Die Richtlinien stehen jetzt; ob dieses Modul darüber hinaus etwas trägt, ist ungemessen. |

**Nicht in dieser Tabelle, weil kein Modul:** `AddCQRS` (aus `Girder.Application`) ruft jeder Dienst selbst, und `worker`-eigene Pipeline-Glieder (`TransaktionsBehavior`, identity zusätzlich `VersandBehavior`) hängen daneben.

### Und dieselbe Frage an unseren Eigenbau

| Eigenbau | bleibt? | gemessen |
|---|---|---|
| `Bremse.cs` | **ja** | Keine von Girders drei Ratenbegrenzungen kann *je Herkunft statt je Benutzer* und *den Kopf des Aufrufers nicht glauben*. Girders **Zähler** ist gut und wird benutzt. |
| `Korrelation.cs` (Gateway) | **zu prüfen** | Girders `UseCorrelationId()` tut dasselbe für Dienste. Das Gateway hat keine `ServiceDefaults` — deshalb steht dort eigenes. Ob das Gateway `AddWorkerTransferDefaults` bekommen sollte, ist offen. |
| `Gesundheit.cs` (Gateway) | **ja** | Ocelot beendet die Kette, ein `MapGet` dahinter läuft nie — gemessen. Girders `AddHealthChecks()` registriert Endpunkte, keine Middleware. |
| `Navigation.cs` | **ja** | Die `Sec-Fetch-Dest`-Regel hat in Girder keine Entsprechung. |
| `ProblemDetailsMiddleware` | **zu prüfen** | Girder hat `GlobalExceptionHandlingMiddleware` mit eigener Fehlergestalt. Eine Gestalt über alle Dienste ist der Grund für unsere — zu belegen, dass Girders nicht dasselbe kann. |
| `Outbox` | **ja** | Girder hat keine. |
| `Wanderung`, `ZugriffsCookie`, `Skills`, `Contracts.*` | **ja** | Fachlichkeit, kein Nachbau. |

## The route map is a test, not a checklist

[`docs/routenkarte.yml`](docs/routenkarte.yml) records, for every endpoint reachable through the gateway, what it answers in the **three** principals ADR-0017 distinguishes: no token, a person with no company (`tenant_id` null), and someone acting for a company. The middle one is the one people forget — on a transfer market a person without a company is the *normal* case, and the rows where the middle and right columns differ are exactly where ADR-0017 is doing work.

Two things drive it, and they answer different questions:

- **`RoutenkarteTests`** (in the gateway suite, no stack needed) asserts the map is **complete**: every route in `ocelot.json` has at least one entry. Add a route without an entry and it goes red. Without this, the map would be correct exactly until the next endpoint.
- **`scripts/routenkarte.sh`** (`make routenkarte`, needs `make up`) drives all 315 answers against the running stack. It is a script rather than a test suite on purpose: a suite that needs a stack skips itself without one, and a skipped test looks exactly like a passing one.

The reason it is a test at all: `scripts/k8s-up.sh` claimed `GET /jobs` answers 200. It answers **401** — a job list sits behind login — and the script had never run, so nobody found out. A list nobody drives is wrong the day after it is written.

Building the map found three things a checklist would have blessed: five endpoints answered **500** on a malformed body (one of them `/auth/login`, where a missing password reached the password hasher); `/companies/withdrawal` was routed publicly and answered 401, confirming an internal door exists; and `GET /notifications` answers **405**, which reveals a path the service deliberately hides behind a 404. The first two are fixed. The third is written down in the map, not fixed.

### The gateway

Ocelot 25. `ocelot.json` carries its own reasoning in `//` comments — measured: .NET's JSON configuration provider skips them.

**One origin makes `/jobs` mean two things** — the API resource *and* the page; same for `/applications`, `/transfers`, `/github`. Compose hides this (the UI on `:5173`); through the gateway, typing `…:8090/jobs` returned raw JSON. What makes it easy to miss: **clicking inside the app works** — the router switches in the browser and never asks — so only the **deep link and the reload** break, which is exactly what people share and what happens after a crash.

The fix is the header that actually answers the question. `Sec-Fetch-Dest: document` is sent only on a top-level navigation (`fetch` sends `empty`, curl sends nothing), so `Navigation` middleware rewrites such a request onto the UI prefix. It is **middleware, not a route**: in Ocelot a literal path beats a placeholder regardless of `Priority`, so a `/{all}` catch-all could never win against `/jobs`. Health probes are middleware for the same reason — Ocelot terminates the pipeline, so a `MapGet` behind it never runs.

Route order in `ocelot.json` is pinned by `ReihenfolgeTests` against a *reversed* fixture. A probe that merely set all priorities equal stayed green because file order happened to be right; only reversing the order exposed it, and eight routes fell.

### Frontend

`apps/web` (Vite + React 19 + TanStack Query + TanStack Router) consumes `@workertransfer/ui` (`packages/ui` — hand-written CSS with `--wt-*` custom properties; no Tailwind, no Radix, no component library). `Switch` is a `button[role="switch"]`, not a checkbox: a checkbox promises the change applies on submit, and for a consent toggle that difference is not cosmetic.

**Every route path is English — no German, no mix**: `/`, `/overview`, `/login`, `/register`, `/verify`, `/invitation`, `/profile`, `/portfolio`, `/resume`, `/consents`, `/settings`, `/delete-account`, `/market`, `/transfers`, `/candidates`, `/jobs`, `/careers/<slug>`, `/applications`, `/my-data`, `/github`, `/company/team`, `/company/jobs`, `/company/profile`, `/company/transfers`. `/` is the marketing page and redirects a signed-in visitor to `/overview`; before that split one address served both, which left the overview unlinkable and made a screenshot of `/` depend on the session.

The UI is German, but **hardcoded** — there is no i18n layer, and tests assert the German literals directly.

Playwright E2E lives in `apps/web/e2e/` and runs against the **real** compose stack; there is deliberately no `webServer` in `playwright.config.ts`, because spinning up half an environment would abstract away exactly the integration these tests exist for. Without the stack they skip themselves — so check `curl localhost:<port>/health/live` yourself before trusting a green E2E run. `make validate-e2e` names the skips and prints how many journeys ran.

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

### Erasure: the default deletes completely (ADR-0027)

`POST /account/erasure` at identity-service — self only, **no reason field**: demanding a justification from someone who wants to leave is a lever against them. It immediately revokes every session and disables the account, then cascades over the **outbox** to eight recipients (`consent`, `profile`, `resume`, `portfolio`, `applications`, `transfer`, `github`, `notification`) and identity itself, last. `jobs-service` and `companies-service` are **not** recipients — they hold nothing about a natural person, and `LoeschempfaengerTests` goes red the moment any service grows a personal table and is not on the list. That guard reads the **EF model**, not the source, and uses two signals: personal column names *and* the `Personenzeile` annotation.

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

Job skills and profile skills are compared in `apps/web/src/jobs/match.ts`; the result is a checklist ("Du hast 2 von 3 genannten Fähigkeiten: Python ✓ · Kubernetes ✓ · Go ✗") shown **to the person, never to the company**, and it ranks *jobs*, not people.

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

### Planned, not built: scout, advisor, assessment

[`docs/SCOUT-UND-BERATER.md`](docs/SCOUT-UND-BERATER.md) is a **draft**, in the same category as the vision documents: intent, not description. It sketches three future services — `scout-service` (a requirement in, people with checklists and evidence out), `advisor-service` (a person's mandate and their staged conversations with a company), `assessment-service` (a named, bounded task whose evaluation belongs to the process and not to the person) — together with their conditions, the interface rules that carry them, and four Playwright journeys that double as acceptance.

**None of it is built, and no agent builds any of it.** Each needs its own ADR first — the document itself says so, and names the open questions still to settle. It is recorded here so that nobody throws away, while tidying up, something that is about to be needed.

## Conventions that bite

- **Package managers are `dotnet`/NuGet and `pnpm`.** Never `npm`, never `yarn`. There is no Python here any more; if a command in an old document says `uv`, `ruff`, `mypy`, `pytest` or `alembic`, that document is describing the predecessor.
- **Warnings are errors** (`Directory.Build.props`). Central package management via `Directory.Packages.props` — versions go there, not into a `.csproj`.
- **Sharing rule**: only domain-neutral, transport-independent, non-business code goes in `src/shared/`. Profile, company, job, transfer, contract, application and matching models stay inside the owning service. `Contracts.*` holds versioned boundary DTOs, never a shared domain model.
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
- `bugs/` — open Girder debts, each with a reproduction.
- `AGENTS.md` — concise command + convention reference.
