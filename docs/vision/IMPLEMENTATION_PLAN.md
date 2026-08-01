# WorkerTransfer - Master Implementation Plan

Based on kon.txt vision: **AI-first Workforce Operating System** with 30+ shared packages, 20+ microservices, platform builder pattern, AI agents, transfer market, GitHub intelligence, MCP integration.

---

## Current State (Foundation Complete)
- ✅ Python 3.14 + uv workspace
- ✅ worker-core: Entity, ValueObject, DomainEvent, Result, DomainError
- ✅ worker-platform: Config, Logging, Correlation, CQRS (Mediator, Pipeline), Middleware, Health
- ✅ identity-service: Reference service with health endpoints
- ✅ Frontend: React + TanStack Query + @workertransfer/ui (Button, Card)
- ✅ TurboRepo + pnpm workspace

---

## Phase 0: Platform Foundation (Shared Packages) - PRIORITY 1

### Core Packages to Create (30+ packages)

| Package | Purpose | Key Components |
|---------|---------|----------------|
| `worker-core` | Domain primitives | Entity, VO, Aggregate, DomainEvent, Specification, Result, Errors, Guards |
| `worker-shared` | Utilities | Constants, Enums, Time, IDs, Pagination, Money, Address, Phone, Email |
| `worker-config` | Configuration | Env loader, Secrets (Vault), Feature flags, Typed config, Validation |
| `worker-logging` | Structured logging | JSON, CorrelationId, TraceId, RequestId, Context/Audit/Performance loggers |
| `worker-correlation` | Context propagation | CorrelationId middleware, ContextVars, Header propagation |
| `worker-security` | Security headers, crypto | CSP, HSTS, CSRF, XSS, SQLi protection, Encryption, Hashing, Signed URLs |
| `worker-auth` | Authentication | JWT, OAuth2, OIDC, API Keys, Refresh tokens, Session management |
| `worker-authorization` | Authorization | RBAC, ABAC, Casbin, Permission decorators, Policy evaluation |
| `worker-database` | Database layer | SQLAlchemy 2, Alembic, UnitOfWork, Repository, Tenant resolver, Soft delete, Auditing, Outbox |
| `worker-cache` | Caching | Memory, Redis, Distributed, Response cache, CQRS cache, Decorators |
| `worker-cqrs` | CQRS Framework | Command/Query/Handler, CommandBus/QueryBus, Pipeline behaviors (validation, logging, caching, auth, transactions) |
| `worker-events` | Event system | Domain/Integration/Application events, Event store, Outbox/Inbox |
| `worker-messaging` | Message bus | RabbitMQ, Kafka, NATS, Serialization, Routing, Consumers, Publishers, Retry, DLQ |
| `worker-middleware` | HTTP middleware | Auth, AuthZ, Rate limit, Request/Response logging, Correlation, Tenant, Security, Compression, Exception handling, Metrics, Tracing, Localization |
| `worker-health` | Health checks | Liveness, Readiness, Startup, Dependency checks |
| `worker-metrics` | Prometheus metrics | Custom metrics, Histograms, Counters, Gauges |
| `worker-tracing` | OpenTelemetry | Traces, Spans, Context propagation, Sampling |
| `worker-telemetry` | Unified telemetry | Logging + Metrics + Tracing integration |
| `worker-validation` | Validation | FluentValidation style, Request/Domain/Business validators |
| `worker-exceptions` | Error handling | RFC 9457 ProblemDetails, Exception mapping, Error codes |
| `worker-ratelimit` | Rate limiting | Token bucket, Sliding window, Distributed (Redis) |
| `worker-tenancy` | Multi-tenancy | Tenant resolution, Context, Isolation strategies |
| `worker-resilience` | Resilience patterns | Retry, Circuit breaker, Timeout, Bulkhead, Fallback (Tenacity/Polly-style) |
| `worker-storage` | File storage | S3, MinIO, Azure Blob, Local, Signed URLs |
| `worker-search` | Search | Elasticsearch, Meilisearch, Vector search |
| `worker-contracts` | API contracts | Shared DTOs, Events, Messages, Versioning |
| `worker-scheduler` | Job scheduling | Cron, Recurring, Delayed, Distributed scheduler |
| `worker-ai` | AI Runtime | Provider abstraction, Tool calling, Memory, Prompt templates, Streaming |
| `worker-mcp` | MCP Integration | MCP servers/clients, Tool registry, Resource registry |
| `worker-agents` | Agent Runtime | Planner, Executor, Evaluator, Reflection, Knowledge, Vector search |
| `worker-templates` | Document templates | Contract generation, Letters, CV, PDF rendering |
| `worker-notifications` | Notifications | Email (SMTP/SES/SendGrid), SMS, Push, WebSocket, Templates, Queue |
| `worker-email` | Email | Providers, Templates, Queue, Tracking |
| `worker-files` | File handling | Upload, Processing, Validation, CDN |
| `worker-github` | GitHub Intelligence | OAuth, Repo scanner, Skill analyzer, OSS reputation, Contribution graph |

---

## Phase 1: Platform Builder & Service Generator

### Worker CLI (`worker` command)
```
worker new-service <name>          # Creates full Clean Architecture service
worker new-package <name>          # Creates shared package
worker migrate                     # Runs Alembic migrations
worker event <name>                # Generates event classes
worker command <name>              # Generates command + handler
worker query <name>                # Generates query + handler
worker aggregate <name>            # Generates aggregate root
worker entity <name>               # Generates entity
worker valueobject <name>          # Generates value object
worker consumer <name>             # Generates message consumer
worker publisher <name>            # Generates message publisher
worker generate <template>         # Custom generators
```

### PlatformBuilder API
```python
app = PlatformBuilder()
    .add_configuration()
    .add_logging()
    .add_database()
    .add_cache()
    .add_authentication()
    .add_authorization()
    .add_multitenancy()
    .add_correlation()
    .add_healthchecks()
    .add_metrics()
    .add_tracing()
    .add_rate_limiting()
    .add_resilience()
    .add_cqrs()
    .add_events()
    .add_messaging()
    .add_storage()
    .add_security()
    .add_middlewares()
    .build()
```

---

## Phase 2: Core Business Services (20+ Services)

| Service | Domain | Key Features |
|---------|--------|--------------|
| `gateway` | API Gateway | Traefik/Envoy, Auth, RateLimit, Routing, Correlation, Metrics, OTel |
| `identity-service` | Identity | OIDC/OAuth, Sessions, Account lifecycle, Company membership, AuthZ, Audit |
| `profile-service` | User Profile | Candidate-owned profile, Documents, Consent ledger, Career sites |
| `resume-service` | Resume/CV | CV builder, Templates, AI optimization, Versioning, Export |
| `portfolio-service` | Portfolio | Projects, GitHub integration, Verified skills, Evidence |
| `jobs-service` | Jobs | Job posting, Search, Matching, Career site connectors (Greenhouse, Lever, etc.) |
| `applications-service` | Applications | Apply, Track, Manage, AI-assisted applications |
| `transfer-service` | Transfers | Football-style market, Offers, Transfer fees, Negotiation, Contracts |
| `contract-service` | Contracts | Templates, Generation, E-signature (DocuSign/Adobe), Legal review |
| `companies-service` | Companies | Employer profiles, Team, Culture, Benefits, Career sites |
| `career-sites-service` | Career Sites | Personalized landing pages, DNS, CI/CD, Videos, Benefits, Direct apply |
| `ai-service` | AI Platform | Agent runtime, Memory, Tools, MCP, Planner, Executor, Evaluator |
| `messaging-service` | Messaging | Real-time chat, WebSocket, SignalR alternative, Conversations |
| `notifications-service` | Notifications | Email, SMS, Push, In-app, Preferences, Templates |
| `search-service` | Search | Full-text, Vector, Semantic, Filters, Facets, Recommendations |
| `analytics-service` | Analytics | Events, Dashboards, Reports, BI, GDPR-compliant |
| `admin-service` | Admin | User mgmt, Tenant mgmt, Feature flags, Audit, Config |
| `marketplace-service` | Marketplace | Agent marketplace, Third-party agents, Ratings, Reviews |
| `scheduler-service` | Scheduler | Cron, Recurring, Delayed, Distributed |
| `developer-service` | GitHub Intelligence | GitHub/GitLab/Bitbucket OAuth, Skill graph, Repo analysis, AI Scout |
| `github-service` | GitHub Integration | MCP server, Repository scanner, Skill extractor, OSS reputation |

---

## Phase 3: AI Agent Platform

### Agent Categories
**For Candidates (11 agents):**
- Career Coach, Application Expert, CV Optimizer, Cover Letter Generator
- Interview Trainer, Salary Advisor, Transfer Advisor, Skill Analyzer
- Learning Coach, Portfolio Builder, Document Manager

**For Companies (10 agents):**
- Scout Agent, Recruiter Agent, Interview Agent, Candidate Ranking Agent
- Salary Recommendation Agent, Team Analyzer, Talent Discovery Agent
- Skill Gap Agent, Workforce Planner, Offer Generator

**Cross-cutting (2 agents):**
- Negotiation Agent (mediates between parties)
- Contract Agent (generates: Employment, NDA, Termination, Transfer, Amendments)

### AI Architecture
```
AI Runtime
    ↓
Provider Abstraction (OpenAI, Anthropic, Gemini, Ollama)
    ↓
Tool Calling
    ↓
Memory (Short-term, Long-term, Episodic, Semantic)
    ↓
Prompt Templates (Versioned, Tested)
    ↓
Agents (Specialized, Composable)
    ↓
Planner → Executor → Evaluator → Reflection
    ↓
MCP (Model Context Protocol) Integration
    ↓
Knowledge Base + Vector Search
```

---

## Phase 4: Transfer Market (Football-Style)

### Market Statuses
- `Open` → `Listening` → `Unavailable` → `Under Contract` → `Transfer Listed` → `Negotiating` → `Transferred`

### Flow
1. Company shows interest → 2. Makes offer → 3. Offers transfer fee → 4. Offers bonus → 5. Sets start date → 6. Generates contract → 7. Digital signature

### AI Scouts
- Input: "Senior Java Developer with Event Sourcing"
- Scout Agent → Finds 14 candidates → Analyzes CV → Analyzes GitHub → Analyzes LinkedIn → Analyzes Skills → Calculates switch probability → Computes Match Score → Writes proposals

---

## Phase 5: GitHub Intelligence / Developer Intelligence

### Skill Graph from Verified Signals
- Commits, PRs, Issues, Discussions, Releases, Actions, Security, Topics, Tags, Branch Protection, Reviews
- Multi-dimensional scores: Technical Expertise, Architecture, Open Source, Community, Leadership, Documentation, Testing, DevOps, AI, Security

### AI Portfolio Generator
- Top projects → Description → Architecture → Tech stack → Screenshots → Diagrams → Highlights → CV supplement

### AI Code Analyzer (with consent)
- Architecture, Clean Code, Test quality, Documentation, Maintainability, Security, Performance patterns

---

## Phase 6: Frontend Architecture (TypeScript 7, React 19)

### Structure
```
apps/web/
├── Design System (packages/ui)
│   ├── Buttons, Cards, Dialogs, DataGrid, Forms, Theme, Icons, Charts, Motion
├── Shared UI
├── Layouts (Auth, Dashboard, Public, Admin)
├── Shell (App shell, Navigation, Providers)
├── Feature Modules (by domain)
│   ├── Identity, Profile, Resume, Jobs, Applications, Transfers, Contracts, AI, Messaging, Notifications, Search, Analytics, Admin
├── State (Zustand + TanStack Query)
├── API (Generated from OpenAPI, type-safe)
├── Components (Atomic design)
└── Pages (Route-based)
```

### Tech Stack
- Node 24, TypeScript 7, Vite, React 19
- TanStack Router, TanStack Query, Redux Toolkit, Zustand
- React Hook Form + Zod, Tailwind, Shadcn/ui, Radix, Motion
- Storybook, Vitest, Playwright, ESLint, Prettier, Biome

---

## Phase 7: Infrastructure & DevOps

### Docker & Kubernetes
- Multi-stage Dockerfiles per service
- Helm charts / Kustomize
- GitOps (ArgoCD/Flux)
- Environments: dev, staging, prod

### CI/CD (GitHub Actions)
- Matrix builds for all packages
- Typecheck → Lint → Test → Build → Security scan → Deploy
- Contract testing (Pact)
- E2E tests (Playwright)

### Observability Stack
- OpenTelemetry Collector → Jaeger (traces) + Prometheus (metrics) + Loki (logs)
- Grafana dashboards
- Alerting (Prometheus Alertmanager)
- SLO/SLI definitions

---

## Execution Strategy: Parallel Agent Deployment

### Agent Teams (run concurrently)

| Team | Focus | Deliverables |
|------|-------|--------------|
| **Team Foundation** | Shared packages (1-10) | worker-config, logging, correlation, security, auth, authz, database, cache, cqrs |
| **Team Events** | Events & Messaging (11-14) | worker-events, messaging, middleware, health |
| **Team Observability** | Metrics, Tracing, Telemetry (15-18) | worker-metrics, tracing, telemetry, validation |
| **Team Resilience** | Exceptions, RateLimit, Tenancy, Resilience (19-22) | worker-exceptions, ratelimit, tenancy, resilience |
| **Team Storage** | Storage, Search, Contracts, Scheduler (23-26) | worker-storage, search, contracts, scheduler |
| **Team AI** | AI, MCP, Agents, Templates (27-30) | worker-ai, mcp, agents, templates |
| **Team Notifications** | Notifications, Email, Files, GitHub (31-34) | worker-notifications, email, files, github |
| **Team Platform** | CLI, PlatformBuilder, Service Generator | worker CLI, PlatformBuilder, Templates |
| **Team Services** | 20+ Microservices | All business services |
| **Team Gateway** | API Gateway | Traefik/Envoy config, Auth, RateLimit, Routing |
| **Team Frontend** | Web App + Design System | apps/web, packages/ui, Storybook |
| **Team Infra** | Docker, K8s, CI/CD, GitOps | Deployment, Monitoring, Security |

---

## Immediate Next Steps (Start Today)

1. **Create all 30+ package directories with pyproject.toml**
2. **Implement worker-config (configuration foundation)**
3. **Implement worker-database (SQLAlchemy 2 + Alembic + UoW + Repository)**
4. **Implement worker-cqrs (full pipeline behaviors)**
5. **Implement worker-events (domain events + outbox)**
6. **Implement worker-messaging (RabbitMQ + serialization)**
7. **Create worker CLI with service generator**
8. **Generate identity-service from template (full Clean Architecture)**
9. **Set up Docker Compose for local dev (Postgres, Redis, RabbitMQ, MinIO, Jaeger, Prometheus, Grafana)**
10. **Configure GitHub Actions CI pipeline**

---

## Success Criteria

- [ ] `worker new-service transfers` creates production-ready service in <30 seconds
- [ ] All 30+ packages have 100% type coverage (mypy strict)
- [ ] All packages pass ruff format/check, pytest
- [ ] Identity service runs with: Auth, DB, Cache, CQRS, Events, Messaging, Health, Metrics, Tracing
- [ ] Frontend builds, type-checks, tests pass
- [ ] Docker Compose spins up full stack locally
- [ ] CI/CD passes on every PR
- [ ] ADR documentation for every major decision