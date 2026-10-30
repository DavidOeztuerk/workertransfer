# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**This file holds only what every session needs.** Task knowledge lives in skills under `.claude/skills/`, history in [`docs/GESCHICHTE.md`](docs/GESCHICHTE.md). A short rule here is **binding**; the skill named beside it (`→ wt-…`) holds the reason — read it before you change that area. A reference `CLAUDE.md, <section>` in code leads through the heading of that name, here or in a skill.

## What this is

WorkerTransfer is a consent-first talent-mobility platform (applications, direct recruiting, employment transfers, AI-assisted career workflows). It is **.NET 10 on [Noelia](https://github.com/DavidOeztuerk) 7.0.1** — a shared foundation library of this author's, from nuget.org — plus a React app.

Fifteen services and a gateway live under `src/`, their tests under `tests/`, the React app in `web/`.

It was a Python monorepo until August 2026; **everything Python is gone**. The ADRs in [`docs/adr/`](docs/adr/) predate the migration and **still govern** — where one names a Python path, it names the decision. `docs/MIGRATION-STAND.md` records what was *measured*; read it before assuming a shape is arbitrary.

### The layout, so nobody guesses

```
src/            fifteen services, gateway/, shared/
tests/          twenty test projects
web/            the React app — deliberately not under src/
docs/  bugs/  deploy/  docker/  scripts/  .github/
WorkerTransfer.slnx  Directory.Build.props  Directory.Packages.props  NuGet.Config
package.json  pnpm-lock.yaml  pnpm-workspace.yaml  turbo.json  tsconfig.base.json
```

`web/` is the *whole* frontend, deliberately not under `src/`, with its own lockfile; anything that says `apps/web` or `packages/ui` describes the predecessor. → `wt-frontend`

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
| `advisor-service` | 8013 | the mandate — four values; the stages live in the ledger |
| `assessment-service` | 8014 | the work sample — the effort in hours, and one evaluation the person always reads |
| `messages-service` | 8015 | one conversation per process — and drafts only a human being releases |

The gateway (`src/gateway`, port 8090) is the single entrance. `src/shared/` holds seven things and no more: `ServiceDefaults` (the one call every service makes), `Outbox`, `Skills`, `DocumentStore` (bytes for certificates, never a PDF the server rendered — `Ablage` until 24.09.2026), and three `Contracts.*` packages (`Identity`, `Consent`, `Erasure`) that carry versioned boundary DTOs — never a shared domain model. The eighth, `Nachweis`, collapsed into Noelia with ADR-0045; the checks WorkerTransfer answers itself live in `ServiceDefaults/Checks`.

The vision documents in [`docs/vision/`](docs/vision/) describe a much larger future state. **Treat them as intent, not description.**

## Der Umstieg auf Noelia ist gefahren (ADR-0045)

**Girder und Noelia sind dieselbe Codelinie.** `MIGRATION.md:781` im
Noelia-Baum heißt *„4.4.3 code line → Noelia 5.0.0"*, und das ist die einzige
Tür zwischen den beiden Identitäten.

**Diese Entscheidung hat ein Verfallsdatum.** Am Tag, an dem das erste echte
Konto hier liegt, gilt wieder, was `MIGRATION.md` §*„Before the first Noelia
process starts"* sagt — `docs/AUFTRAG-NOELIA-UMSTIEG.md` bleibt genau dafür im
Baum stehen.

**Eine Versionsnummer hinter „Girder" ist Geschichte, kein Verweis** — eine
Messung an einer veröffentlichten Version wird nicht umbenannt.

```bash
make analyze          # noelia analyze über die Zusammensetzung; steht in `make validate`
```

Verlauf: `docs/GESCHICHTE.md`. → `wt-noelia-module`

## Nachweis und KI-Pflichten

**Belege, keine Konformität.** Ein Programm zeigt Vorhandensein, Zeitpunkt und Unversehrtheit — nie Angemessenheit; nichts sagt „konform", „zertifiziert" oder „erfüllt Art. X". Das gilt für jeden Satz, der in ein Dokument oder auf eine Seite gerät. → `wt-nachweis`, `/nachweis`

## Commands

Everything has a `make` target, and the target is the twin of what CI runs. `make help` lists them.

```bash
make env            # FIRST, in a fresh clone: writes .env, rolls four secrets and generates the token key pair
make check          # the gate: build, then test, then the frontend — fail-fast
make build          # dotnet build. Warnings are errors (Directory.Build.props)
make test           # the suites, ONE AT A TIME (see below)
make check-web      # pnpm check + test + build
make fix            # dotnet format
make validate       # like check, but runs through and reports every red step
make up / make down # the whole stack in docker compose
make images         # both shipped images — the local twin of the CI job
make route-map      # every endpoint × four principals, against the running stack
make e2e            # the Playwright journeys, the platform model switched off as in CI (route-map too)
make ai-check       # does the platform's model answer? profile help and a cover letter, the real way
make evidence       # three dated, signed evidence documents (ADR-0044)
make evidence-check # every check in every service; red on an unkept promise
make k8s-up / k8s-down / k8s-lint
```

**Build and test in separate invocations.** Chained (`build && test`), the Testcontainers suites fail *and look like real test failures*. This has cost time more than once.

**Never `dotnet test` over the solution** — every suite fails in a millisecond with `TypeInitializationException`. `scripts/test-dotnet.sh` runs them one at a time and prints how many tests ran. → `wt-tore`

Single suite / single test:

```bash
dotnet test tests/WorkerTransfer.Identity.Tests/WorkerTransfer.Identity.Tests.csproj --no-build
dotnet test tests/WorkerTransfer.Consent.Tests/... --no-build --filter "FullyQualifiedName~Widerruf"
```

Restore needs no login; `NuGet.Config` pins that exactly one source answers `Noelia.*`. **403 means authenticated and refused, 401 means never authenticated at all.** → `wt-ci`

### Configuration comes from the environment

`.env` is git-ignored; `.env.example` ships the secrets **empty**. **A built-in default *is* the secret**: compose uses `${X:?…}`, never `${X:-…}`. `EnvironmentFile.Load()` is the first line of every `Program.cs`, before `CreateBuilder`, and never overwrites a set variable. **An `.env` written before 25.09.2026 carries German variable names** — rename them by hand. → `wt-stack`

### The stack

`docker compose up` is the whole environment; each service migrates its own schema on start; a code change needs `docker compose up -d --build <service>`. **`app.UseTokenRevocation()` stands in `UseWorkerTransferDefaults` and has to**, or a sign-out reaches no other service. `/health/ready` answers for the database and names no value. → `wt-stack`

### The Noelia Control Plane — on the host, deliberately not part of `make up`

`make control-plane` (needs `make up`); with sign-in `make keycloak && make control-plane-oidc`. The licence comes from the environment, never from a file. → `wt-control-plane`

### Kubernetes — a staging environment on your own machine

**Compose and kind cannot run at the same time** (same ports) — `make down` first. `replicaCount: 1` is a decision; the web image takes its URLs at runtime, never as a build arg. → `wt-k8s`

### The brake on the auth endpoints, and why it lives in the gateway

Five `/auth/*` paths are braked in the gateway, **per origin, never per email address**; **`X-Forwarded-For` is never read**; the defaults stay `0`. → `wt-gateway`

### github-service: the header that made it never work

Every GitHub request carries a `User-Agent`; evidence stores topics and the language set, never byte counts; no OAuth token is kept. → `wt-github`

### CI

**No audit runs with `continue-on-error`**, a scan that examined nothing is red, and there is no `dependabot.yml`. → `wt-ci`

### Branches and the one path back

`feature → develop → main`, never a feature branch straight into main.

A **hotfix** is the single exception: it branches off `main`, merges into `main`, and its branch **must** start with **`hotfix/`** — `backmerge.yml` fires on that prefix only, so a `fix/…` branch never comes back to develop. → `wt-ci`

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

`ServiceDefaults.AddWorkerTransferDefaults()` holds only what must not differ between services (token check, wire format of a failure, pipeline order); every decision — database, repositories, cache, outbox — stays in the service's own composition root. → `wt-noelia-module`

### CQRS

**Our own `ICommandMessage` / `IQueryMessage`** (per service in `Application/Messages/`) over MediatR's `IRequest`, never Noelia's `ICommand<T>` — a second error envelope beside RFC 9457. `TransactionBehavior` wraps **commands only**. → `wt-noelia-module`

### Errors

RFC 9457 problem documents, everywhere, from `ServiceDefaults.ProblemDetailsMiddleware`. Every response carries a `correlationId`. **No values in logs** — shapes, not contents. Noelia logs no values either, neither redacted nor scrubbed; do not build that back.

An endpoint filter that has already written a response must return `Results.Empty`, never `null`: with headers sent, the framework writes a JSON null after them, which tears the connection on a POST-with-body and is invisible on a GET except in the log.

**A validator message names the rule, never the value** — `ValidationBehavior` logs it, and FluentValidation's defaults insert `{PropertyValue}`. **Naming a log property `Email` would make it unreadable.** → `wt-noelia-module`

### Request context

Correlation and tenant flow through the request, set by middleware in `ServiceDefaults`. **Tenant identity must never come from a browser header in production.**

- **A tenant is a company, and a natural person has none** (ADR-0017, `Capacity`). The consent ledger has no tenant column by design — do not "fix" that.
- The client names the company, the server decides: `POST /auth/company/{id}` checks membership before it mints a tenant. The token's claim set is pinned **complete**; roles are never in it; it leaves only as the `httpOnly` cookie. Every service accepts a `Bearer` header **or** the `access` cookie. Only identity-service holds `Jwt__PrivateKey`; every other service holds the public key alone (ADR-0046).
- **An internal door behind the shared secret proves that a *service* asks, not that it *may*** — the caller fetches the ledger check first.
- **Hiding is not access control.** Company rights are checked at the endpoint, before the body; the role is read per request, uncached; `member` < `recruiter` < `admin`, **in doubt, `member`**; **a silent role lookup answers 503, never 403**.
- Register and resend answer identically whether or not the address is known; a company is created at registration and nowhere else.

→ `wt-identity`

### Persistence

EF Core, one database per service — **no shared database and no cross-service repository abstraction** (ADR-0004). Migrations apply at startup and retry **only transient failures**.

**Aggregates come back detached from the repositories.** A mutation reaches the database only via an explicit save. Forgetting it costs nothing at test time and silently loses the write in production.

**And the save itself needs `.AsTracking()`**, because every context runs `NoTracking` — without it nothing is written and the caller still gets its `204`. → `wt-tore`, traps 3 and 4

A table whose key *is* the person carries `SubjectKeyedTable`, or the erasure guard does not see it. → `wt-consent`

### The outbox

`WorkerTransfer.Outbox` records an *intent* in the **same transaction** as the domain change; a dispatcher delivers it with retries (ADR-0025). The promise it keeps is the old one — a failed mail must never topple the transaction — kept differently.

The table deliberately holds **no content**, only a user id and a kind. An outbox is durable storage and ends up in every backup, so a payload column would be an invitation to write message text into it. **Never carry an email address into it.** Giving up means leaving the row, never deleting it. Delivery is **at-least-once**. There is no broker, and none is planned.

### The Noelia modules

**A module is called unless there is a measured reason against it**, and the reason lives in `.Without(module, reason)`; the startup report wins over any table. → `wt-noelia-module`

### `SovereignPlatform` — angenommen, und die Hosts kommen aus der Konfiguration

The egress guard **refuses**. Allowed hosts are **derived from configuration, never listed in code**; AI targets are declared by the operator in `Draft__AllowedTargets__*`. **A host that comes from data connects only through `ServiceDefaults/PublicAddress`** — public addresses at connect time, no redirect off the domain. Noelia's audit trail is refused — ours is `EfAuditTrail`, in the transaction. → `wt-noelia-module`

### Und dieselbe Frage an unseren Eigenbau

**A hand-written replacement outlives its reason** — each one beside Noelia is justified by measurement. → `wt-noelia-module`

## The route map is a test, not a checklist

Every route in `ocelot.json` needs an entry in [`docs/routenkarte.yml`](docs/routenkarte.yml) (`RouteMapTests`); `make route-map` drives it. → `wt-gateway`

### The gateway

**The gateway serves the API and nothing else** (ADR-0040); route order is pinned by `RouteOrderTests`. → `wt-gateway`

### Frontend

**A colour literal in a component is a defect.** **A consent toggle is a switch, never a checkbox.** **Every text a person reads comes from a catalogue** (ADR-0031); route paths are English; **a problem document stays English on the wire**. → `wt-frontend`

## The rules that carry the design

These are the parts that are easy to undo by "cleaning up". Each has a reason, and the reason is the point. Every line is binding; its skill holds the reason.

- **Consent is read synchronously and never cached (ADR-0013)** — a withdrawal takes effect on the very next read; `/check-batch` changes nothing about that; `/check` carries no reason; there is no `POST /consent/delete`. → `wt-consent`
- **Visibility lives in the ledger, nowhere else (ADR-0020)** — **404** hidden or absent (byte-identical), **403** no active company, **503** the ledger is silent; the request is not the permission. → `wt-consent`
- **Erasure: the default deletes completely (ADR-0027)** — no reason field; a service that grows a personal table joins `ErasureRecipient.Services`; `Retention.*` is a `static readonly` `false`, never a setting; a dead recipient blocks completion. → `wt-consent`
- **The skill vocabulary renames, it never infers (ADR-0023).** → `wt-person-rules`
- **Fit is computed in the browser and exists nowhere else** — no score, percentage or match endpoint. → `wt-person-rules`
- **The AI seam drafts on request and stores nothing (ADR-0024)** — nothing writes by itself; own key, else the platform's model, a silent identity-service refused (ADR-0051); a company's own server only on its proven domain; known values leave as placeholders; no memory, vector store, background suggestion or ledger entry. → `wt-ai-seam`
- **ADR-0022 forbids exactly three things** — a number that summarises a person, derived attributes without a basis, silent completeness. Requirement in, evidence out. → `wt-person-rules`
- **scout-service: ticks and evidence, never a number** (ADR-0036). → `wt-scout`
- **advisor-service: the mandate is a view, not a second store** (ADR-0037). → `wt-advisor`
- **assessment-service: the effort in hours, and one evaluation the person reads** (ADR-0042). → `wt-assessment`
- **messages-service: one conversation per process, and a draft goes out only released** (ADR-0047). → `wt-messages`
- **The way to the current employer: two consents** (ADR-0050); without both, one 404. → `wt-transfer`
- **Adverts from a company's ATS: official reads only**, an unproven source imports nothing (ADR-0052). → `wt-jobs-sources`
- **Signing in with Google, Microsoft, Apple: never linked by the address** (ADR-0053). → `wt-identity`
- **Files from Google Drive and Dropbox: only the chosen one, fetched in the browser** (ADR-0054). → `wt-resume-documents`
- **Suggestions from a person's own documents**: read on trigger, never at upload, never over the network; the index falls with the erasure (ADR-0043). → `wt-resume-documents`
- **The evidence report: Belege, keine Konformität (ADR-0044).** → `wt-nachweis`
- **Still not built: nothing from that draft** — `docs/SCOUT-UND-BERATER.md` is a draft. → `wt-scout`

## Conventions that bite

- **Code is written in English. All of it, backend and frontend.** The wire and the database never move in a rename, and **a settings property name IS the environment key** — rename it everywhere in one pass or not at all, then run `ComposeKeyTests`. → `wt-naming`
- **Package managers are `dotnet`/NuGet and `pnpm`.** Never `npm`, never `yarn`. There is no Python here any more; if a command in an old document says `uv`, `ruff`, `mypy`, `pytest` or `alembic`, that document is describing the predecessor.
- **Warnings are errors** (`Directory.Build.props`). Central package management via `Directory.Packages.props` — versions go there, not into a `.csproj`.
- **Sharing rule**: only domain-neutral, transport-independent, non-business code goes in `src/shared/`. Profile, company, job, transfer, contract, application and matching models stay inside the owning service. `Contracts.*` holds versioned boundary DTOs, never a shared domain model.
- **A service-to-service body is a typed contract, never an anonymous object.** → `wt-tore`
- **No secrets, tokens, CVs, contracts or raw source code in the repo or in logs.**
- **No detour around a Noelia bug** — correct code, a red test, a ticket in `bugs/`. → `wt-noelia-module`
- **Run counter-probes, and make sure the break actually compiles** — a build error reads in the output like a passing test.
- **After a counter-probe, build with `--no-incremental`** — and after the *revert*, not only after the patch. → `wt-tore`
- **A counter-probe that does not fall reveals a weak test, not correct code.** → `wt-tore`
- **Docker must be running**, or the integration suites skip themselves — and a skipped integration test looks exactly like a passing one. `scripts/test-dotnet.sh` prints the counts for this reason.
- **Never build images and run tests at the same time.** They contend, and the failure looks like a test failure.
- **And a cold stack is the *other* cause with the same symptom** — run the same suite on the unchanged tree, and **do not raise the timeout**. → `wt-tore`
- **A comment earns its place when its absence would let someone build a defect back in. Everything else retells the code.** A reason lives at the line, not in an ADR; comments are never thinned with a mechanical filter; a copy of a reason goes only per site, never per pattern. Derivation: `docs/GESCHICHTE.md`
- New cross-cutting architectural decisions get an ADR in `docs/adr/`.

## Skills

`wt-tore` (builds, tests, counter-probes) · `wt-naming` (renames) · `wt-stack` · `wt-control-plane` · `wt-k8s` · `wt-ci` · `wt-gateway` (brake, route map) · `wt-noelia-module` (composition, egress, `bugs/`) · `wt-frontend` · `wt-identity` · `wt-github` · `wt-consent` (ledger, visibility, erasure) · `wt-person-rules` (ADR-0022, vocabulary, fit) · `wt-ai-seam` · `wt-scout` · `wt-advisor` · `wt-assessment` · `wt-messages` · `wt-transfer` · `wt-jobs-sources` · `wt-resume-documents` · `wt-nachweis`

## Key docs

- `docs/MIGRATION-STAND.md` — what was decided and, more usefully, what was *measured*.
- `docs/GESCHICHTE.md` — the history moved out of this file on 30.10.2026.
- `docs/architecture.md` — service shape, sharing rule, delivery sequence.
- `docs/product-scope.md` — consent, AI, data-acquisition and security constraints. Read before touching anything consent- or AI-related.
- `docs/adr/` — the architecture decision records. They predate the migration and still govern.
- `docs/SCOUT-UND-BERATER.md` — planned, not built. See above.
- `docs/glossary.md` — the terms.
- `docs/NAMING.md` — the stem table and the six places where an identifier is also a contract.
- `docs/DRITTANBIETER.md` — every third-party system, built or to come, with its state (18.10.2026).
- `bugs/` — Noelia debts, each with a reproduction. → `wt-noelia-module`
- `AGENTS.md` — concise command + convention reference.
