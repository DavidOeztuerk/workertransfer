# Architecture

## Direction

WorkerTransfer will grow into a platform for candidate-owned profiles, applications,
company recruiting, consensual direct contact, employment-transfer workflows,
documents, and AI-assisted review. The platform supports the business process; it
does not make employment, ranking, or legal decisions autonomously.

## Repository model

```text
workertransfer/
├── apps/                     deployable entry points
│   └── identity-service/
├── packages/                 technical platform packages only
│   ├── worker-core/          framework-free domain primitives
│   └── worker-platform/      HTTP and application cross-cutting concerns
├── docs/                     ADRs, architecture, product constraints
└── .github/workflows/        repeatable CI
```

An `uv` workspace gives all Python packages one lockfile. Packages have their own
`pyproject.toml` files and use explicit workspace dependencies.

## Service shape

Every business service follows the same inward dependency direction:

```text
Presentation  ->  Application  ->  Domain
     |                              ^
     └-------- Infrastructure -------┘
```

- **Presentation** holds HTTP, message-consumer, worker, and future gRPC adapters.
- **Application** holds commands, queries, handlers, orchestration, ports, and
  authorization requirements.
- **Domain** holds aggregates, entities, value objects, domain policies, and domain
  events. It has no FastAPI, database, or transport dependency.
- **Infrastructure** implements application ports: database repositories, message
  transport, storage, provider clients, and cache adapters.

`Contracts` are versioned boundary types, not a fifth Clean Architecture layer.
They are introduced per integration and must not become a shared domain model.

## Sharing rule

Only technical, domain-neutral code may move into `packages/`:

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

The reference `identity-service` deliberately exposes only health probes. Identity
credentials, OAuth/OIDC, sessions, and authorization are a subsequent vertical slice;
adding an unaudited partial authentication flow would create misleading security.

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
