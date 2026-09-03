# Architecture

## Direction

WorkerTransfer will grow into a platform for candidate-owned profiles, applications,
company recruiting, consensual direct contact, employment-transfer workflows,
documents, and AI-assisted review. The platform supports the business process; it
does not make employment, ranking, or legal decisions autonomously.

## Repository model

```text
workertransfer/
├── src/                      eleven services, the gateway, and what they share
│   ├── identity-service/     accounts, sessions, companies (the reference shape)
│   ├── consent-service/      the ledger everything else leans on
│   ├── <nine more services>/ profile, resume, portfolio, jobs, applications,
│   │                         companies, transfer, notification, github
│   ├── gateway/              Ocelot — the single entrance
│   └── shared/               ServiceDefaults, Outbox, Skills, Contracts.*
├── tests/                    one suite per service, plus Gateway, Outbox,
│                             Skills and Ganzes (cross-service guards)
├── web/                      React app — the whole frontend, own lockfile
├── docs/                     ADRs, architecture, product constraints, vision
├── bugs/                     open Girder debts, each with a reproduction
├── deploy/                   Helm chart and kind cluster
├── docker/                   one service Dockerfile, two web ones, one entrypoint
├── scripts/                  database bootstrap, test runner, k8s scripts
└── .github/workflows/        CI: .NET, frontend, images, dependency audit
```

The platform is **.NET 10 on Girder 3.0.1**, a shared foundation library from
GitHub Packages. It replaced a Python (`uv`) monorepo in August 2026; the
translation was done by hand, service by service, and `docs/MIGRATION-STAND.md`
records what was measured along the way.

`src/shared/` holds six things and nothing else: `ServiceDefaults` (the one call
every service makes), `Outbox`, `Skills`, and three `Contracts.*` packages. There
is no kernel of unused libraries — a package here has a consumer or it does not
exist.

## Service shape

Every business service follows the same inward dependency direction:

```text
Presentation  ->  Application  ->  Domain
     |                              ^
     └-------- Infrastructure -------┘
```

- **Presentation** (the `Api` project) holds HTTP endpoints and their filters.
  All dependency wiring sits behind one `Add<Service>Infrastructure()` per
  service — its composition root, not a fluent builder (ADR-0003).
- **Application** holds commands, queries, handlers, orchestration, ports, and
  authorization requirements.
- **Domain** holds aggregates, entities, value objects, domain policies, and domain
  events. It has no ASP.NET, EF Core, or transport dependency. Repository
  *interfaces* live here; their implementations live in Infrastructure.
- **Infrastructure** implements application ports: database repositories, message
  transport, storage, provider clients, and cache adapters.

`Contracts` are versioned boundary types, not a fifth Clean Architecture layer.
They are introduced per integration and must not become a shared domain model.

## Sharing rule

Only technical, domain-neutral code may move into `src/shared/`:

- context propagation, observability, configuration, error mapping, resilience
- CQRS dispatch abstractions and test tooling
- transport-independent security primitives

Profile, company, job, transfer, contract, application, and candidate-matching
models remain inside the owning service. There is no shared database and no shared
repository abstraction that exposes another service's data.

## Platform baseline already implemented

`worker-platform` supplies a service factory with:

- typed settings loaded from the environment
- JSON logs enriched with correlation and tenant context
- request correlation IDs that are returned in every HTTP response
- tenant context that is empty by default; a header resolver is opt-in and allowed
  only for local/development/test use
- security response headers
- liveness and readiness probes
- consistent `application/problem+json` errors
- an explicit asynchronous CQRS mediator with ordered pipeline behaviours

`identity-service` is the reference service and now carries a complete auth vertical
slice (Phase 2): registration, login, refresh and logout with bcrypt hashing and HS256
JWTs delivered as `httpOnly` cookies, a server-side session ledger for refresh-token
rotation, claim-based tenant resolution, and audit events written inside the same
transaction as the command that caused them. See ADR-0006 through ADR-0012.

OIDC-as-provider is deliberately *deferred*, not dropped — ADR-0008 records why a
self-hosted password flow was the right first slice and what the upgrade path is.

## Delivery sequence

1. **Foundation (current):** workspace, coding standards, platform primitives,
   reference service, CI.
2. **Identity and tenancy:** OIDC/OAuth, sessions, account lifecycle, company
   membership, authorization, audit events, PostgreSQL migrations.
3. **Candidate core:** user-controlled profile, documents, consent ledger, jobs,
   applications, and approved career-site connectors.
4. **Talent mobility:** employer offers, worker consent, negotiation states,
   transfers, contract review/generation workflow, and e-signature integration.
5. **Intelligence:** GitHub OAuth and user-approved public-signal ingestion,
   transparent skill evidence, search, and human-reviewed recommendations.
6. **Scale-out:** transactional outbox/inbox, brokered integration events, cache,
   files, search, gateway, frontend, observability, and deployment automation.

Services only split when independent deployment, data ownership, scalability, or team
ownership justify it. The repository layout supports that outcome without forcing
premature network boundaries.
