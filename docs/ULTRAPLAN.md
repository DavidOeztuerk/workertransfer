# WorkerTransfer — Ultraplan

Ein stufenweiser Masterplan zum Ausbau des WorkerTransfer-Repositorys zu dem in
[`docs/vision/kon.txt`](vision/kon.txt) beschriebenen AI-first Workforce
Operating System.

Dieses Dokument ist ein **lebendes, ausführbares Dokument**. Jede Phase hat eine
Definition of Done (DoD). Wir arbeiten sie Phase für Phase ab, committen nach
jedem Schritt, und halten die Definition-of-Done-Kriterien ein, bevor wir weitergehen.

> **Der Leitsatz, der am leichtesten verloren geht** — `kon.txt`, Regel Nr. 1:
> *„Wir schreiben keinen einzigen Microservice, bevor die Plattform existiert."*
> Der Sinn von Phase 0/1 war, dass `worker new-service <name>` einen **fertigen**
> Service erzeugt, damit Service 3 bis 21 nie handkopiert werden. Diese Regel war
> bereits gerissen: `apps/consent-service` wurde von Hand aus `identity-service`
> kopiert, und der Generator erzeugte Code, der nicht einmal parste (Phase 2.5/A6).
> Neue Services kommen aus dem Generator, nicht aus Copy-Paste.

---

## 0. Stand (verifiziert am 31.07.2026)

**Phasen 0, 1, 2 und 2.5 sind abgeschlossen; Phase 3 Sub-step 3.1 ist in Arbeit.**
Der Stand pro Phase steht in [`ROADMAP.md`](ROADMAP.md) — dort auch die vollständige
Liste der in Phase 2.5 gefundenen Fundament-Defizite und der offenen Folge-Punkte.

Kurzfassung dessen, was heute wirklich läuft:

- **`worker-platform`** (Kernel, 11 Module): `create_api_app()` mit Compose-Hook
  (`tenant_resolver`, `auth_middleware`, `routers`), Correlation-/Tenant-/
  Security-Middleware als rohes ASGI, async CQRS-`Mediator`, RFC-9457-Problem-Details,
  Health-Router, `PlatformSettings` (`WORKER_`-Prefix), JSON-Logging mit Kontext.
- **`identity-service`** — vollständiger Auth-Slice: `POST /auth/{register,login,
  refresh,logout}`, `GET /me`, User-/Session-/Audit-Aggregate, bcrypt + HS256,
  Alembic `0001`, Testcontainers-Integrationstests. Token wird per Header **oder**
  `access`-Cookie akzeptiert.
- **`consent-service`** — ~10 %: Scaffold, Alembic-Gerüst (`versions/` noch leer),
  Domain-Value-Objects. `compose_api.py` ist noch ein Platzhalter.
- **`apps/web`** — zwei Routen (`/`, `/login`), Session-State über TanStack Query,
  `packages/ui` mit `Button`/`Card`. Kein i18n-Layer.
- **34 `worker-*`-Pakete**, die meisten mit genau einem Smoke-Test und **ohne**
  Produktionskonsumenten. `worker-ai`/`worker-files` sind aus dem Workspace
  exkludiert, `worker-github` ist unimportierbar.
- **Gates**: `make check` = ruff format → ruff check → mypy → pytest → `pnpm check`
  → `pnpm test`. CI hat einen Backend- **und** einen Frontend-Job.
- **Lokal**: `docker compose up --build` startet Postgres, jeden Service und die
  Web-App. Jeder Service-Container migriert sich beim Start selbst; ein separates
  Skript gibt es nicht. Ein neuer Service braucht kein eigenes Dockerfile —
  `docker/service.Dockerfile` ist geteilt und nimmt `SERVICE_DIR` als Build-Arg.

<details>
<summary>Historischer Stand vom 12.07.2026 (Ausgangspunkt von Phase 1)</summary>

### Läuft und ist real
- **`worker-platform`** (493 LOC, 11 Module) — der reife Kernel:
  `create_api_app()` Factory, `CorrelationIdMiddleware`,
  `TenantContextMiddleware`, `SecurityHeadersMiddleware`, async CQRS `Mediator`
  mit `PipelineBehavior`, Health-Router, RFC-9457 Problem-Errors,
  `PlatformSettings` (pydantic-settings, `WORKER_`-Prefix).
- **`identity-service`** — Referenzservice, `/health/live` + `/health/ready`,
  läuft auf Port 8001 via `worker-identity`.
- **`worker-core`** — `Entity`, `ValueObject`, `DomainEvent`, `Result`,
  `DomainError`.
- **`worker-cli`** (421 LOC) — Typer-CLI mit Befehlen
  `new-service / new-package / command / query / entity / aggregate / valueobject /
  event / consumer / publisher / migrate / upgrade` + vollständige
  Template-Bausteine unter `src/worker_cli/templates/`.
- **~30 `worker-*` Bibliotheken** — single-file Module mit echter Implementierung
  (worker-ai 243, worker-github 313, worker-resilience 160, worker-storage 181,
  worker-files 197, worker-templates 174, worker-search 199 u. a.).
  **Alle Heavy-Dependencies sind installiert** (openai, anthropic, chromadb,
  qdrant, githubkit, weasyprint, casbin, aio-pika, langchain, opentelemetry,
  sqlalchemy/alembic, redis, structlog…).
- **Frontend** — React 19 + TanStack Query Skeleton, `@workertransfer/ui`
  (Button, Card), deutschsprachig. Läuft unter `apps/web`.
- **`.opencode/skill/`** — 7 Skill-Markdown-Dateien (clean-architecture,
  ai-agents, database-layer, auth-authz, frontend, python-monorepo, devops).

### Defizite und Risiken (faktenbasiert)
- **CI ist ROT.** ruff meldet **46 Fehler**, mypy meldet **311 Fehler in 31
  Dateien**. Jeder PR schlägt aktuell fehl.
  - mypy Schwerpunkte: worker-scheduler (44), worker-ai (35), worker-resilience
    (28), worker-validation (22), worker-messaging (22).
  - mypy Fehlerarten: `no-untyped-def` (77×), `type-arg` (51×),
    `no-any-return` (29×), `union-attr` (24×), `attr-defined` (19×),
    `import-untyped` (17×).
  - ruff Fehlerarten: `F401` (20× ungenutzte Imports), `RUF013` (10×
    fehlendes `Optional`), `UP042/RUF012/E501/ASYNC2xx` (je 2×).
- **CLI ist kaputt.** `[project.scripts] worker = "worker_cli.main:app"` zeigt
  auf ein nicht existierendes `worker_cli.main`-Modul — der Code liegt in
  `__init__.py`. `uv run worker` → `ModuleNotFoundError: No module named
  'worker_cli.main'`. Die CLI kann also nie gelaufen sein / wurde nie smoke-getestet.
- **Architektonische Duplikation** — gleiche Konzepte in `worker-platform` UND
  `worker-*` Geschwistern:
  - zwei Correlation-Implementierungen
    (`worker_platform.context` ↔ `worker-correlation`)
  - zwei CQRS-Mediatoren
    (`worker_platform.application.cqrs` ↔ `worker-cqrs`)
  - zwei Health-Systeme
    (`worker_platform.presentation.health` ↔ `worker-health`)
  - drei Settings-Typen (`worker-platform` ↔ `worker-config` ↔ `worker-tenancy`).
- **Nur 7 Tests insgesamt** aber farblich grün — die Testabdeckung ist nahe null.

### Entscheidungen (bestätigt)
1. **Erst Fix & festigen** — Fundament grün machen, dann neue Services.
2. **`worker-platform` = Kernel, `worker-*` = Bausteine** — die Plattform behält
   die laufzeit-reifen Teile (Factory, Middleware, Settings, CQRS-Mediator); die
   `worker-*` Pakete sind isolierte Bibliotheken, die die Plattform beim Aufbau
   nutzt. Plattformseitige Duplikate werden entfernt.
3. **Stufenweiser Masterplan** — Foundation → Identity → Profile →
   Jobs/Applications → Transfermarkt → GitHub-Intelligence → AI Agents →
   Contracts → MCP → Frontend.

</details>

---

## Leitarchitektur (Zielzustand)

### Schichtung (Clean Architecture, kon.txt-konform)
```
Presentation  ->  Application  ->  Domain
     |                              ^
     └-------- Infrastructure ------┘
```
- **Domain** — `worker-core`. Keine FastAPI/ORM/Transport-Abhängigkeit.
- **Application/Presentation cross-cutting** — `worker-platform` (Kernel).
- **Infrastrukturbibliotheken** — die `worker-*` Pakete, isoliert, einzeln
  testbar, von der Plattform beim Aufbau eingebunden (Composition Root).

### Service-Form (alle Services gleich)
Jeder Service hat eigenes `pyproject.toml`, hängt ab von `worker-core` +
benötigte `worker-*` Bausteine + `worker-platform`, definiert eine
service-spezifische `PlatformSettings`-Subklasse und ruft
`worker_platform.presentation.app.create_api_app(settings, builder=...)` auf.
Entrypoints als `[project.scripts]`.

### Composition-Root-Entscheidung (ADR offen)
Statt des in `kon.txt` skizzierten fluent `PlatformBuilder().add_*().build()`
favorisieren wir eine **explizite Composition-Root pro Service** (`compose.py` +
`register_*()`-Funktionen im Platform-Paket). Eine fluent Builder-API verleitet
zu versteckten Abhängigkeiten und falscher Reihenfolge; explizite Registrierung
bleibt mit dem heutigen, expliziten CQRS-Mediatoren konsistent. Die fluent
Builder-Variante wird in `docs/adr/` als abgelehnt dokumentiert, damit das
kon.txt-Ziel nicht als Lücke wahrgenommen wird.

### Vertragsmodell
- `worker-contracts` hält **versionierte Boundary-DTOs und
  Integration-Event-Schemata**, nie ein geteiltes Domain-Modell.
- Pro Integration entsteht ein Connector / Consumer; ohne offizielle API oder
  dokumentierten Feed wird **nicht** integriert (kein Scraping —
  `docs/product-scope.md`).

### Zustands- und Konsens-Regeln (hard constraints)
- **Keine Secrets, Tokens, CVs, Verträge oder Rohquellcode im Repo oder in
  Logs** (CONTRIBUTING.md, product-scope.md).
- **Tenant-Identität kommt nie aus einem Browser-Header im Produktivbetrieb** —
  nur aus authentifizierten Claims. Header-Resolver nur local/dev/test.
- **AI entwirft, Mensch entscheidet** — jeder rechtliche oder externe Versand
  requires human review. Keine autonomen Ranking-/Ablehnungs-/Kontaktentscheidungen
  über Menschen.
- **Keine Nutzungsrechtsverletzung** — nur offizielle APIs/Feeds.

---

## Plot: die zehn Phasen

Jede Phase endet mit einer committbaren, grünen CI und einer klaren DoD.
Phasen mit `[parallelisierbar]` dürfen in Worktrees gleichzeitig laufen, sobald
die Foundation (Phase 1) steht.

### Phase 0 — Repo-Disziplin & Betriebsamskeit (vor Phase 1, parallel)
**Warum:** Damit der Rest lesbar, auffindbar und reproduzierbar bleibt.
- `docs/ULTRAPLAN.md` (dies), plus `docs/ROADMAP.md` als Pull-Through-Index der
  Phasen mit Status.
- `docs/adr/` ADRs für die drei getroffenen Entscheidungen
  (ADR-0002 Kernel-vs-Bausteine, ADR-0003 Composition-Root statt fluent Builder,
  ADR-0004 Vertragsmodell / kein Scraping).
- `AGENTS.md`, `CLAUDE.md`, `CONTRIBUTING.md` um Ultraplan-Verweis ergänzen.
- `.opencode/skill/` um fehlende Skills ergänzen (siehe §Skills unten).
- `docs/glossary.md` — Begriffe: Market Status, Candidate, Employer, Scout,
  Consent-Ledger, Skill-Graph, Match-Score, Verified-Signal.
- `tasks/`- oder `TaskCreate`-Basierter Fortschitt pro Phase.
**DoD:** Roadmap-Index steht, ADRs verlinkt, Skills registriert.

### Phase 1 — Foundation festigen (CI grün, kein neues Feature)
**Warum:** Auf Felsen bauen, nicht auf Sand. 311 mypy + 46 ruff + kaputte CLI.
- 1.1 **ruff aufräumen** — `uv run ruff format . && uv run ruff check . --fix
  --unsafe-fixes` (F401, RUF013, ASYNC2xx, UP04x), Rest manuell. Ziel: 0.
- 1.2 **mypy grün** — pro Paket: Type-Annotationen nachziehen
  (`no-untyped-def`, `type-arg`), `Optional`-Fälle klären, `union-attr`/
  `attr-defined` entweder Typen fixen oder Code korrigieren, `import-untyped`
  via `[tool.mypy]`-overrides für externe untypisierte Libs behandeln,
  `empty-body` mit `...` + `# pragma: no cover`/Protocol konsistent lösen.
  Reihenfolge nach Fehlerlast:.scheduler, ai, resilience, validation,
  messaging, dann der Rest. Ziel: `uv run mypy packages apps` = 0.
- 1.3 **CLI reparieren** — `worker_cli/main.py` anlegen, das `app`-Objekt aus
  `__init__.py` re-exportiert (oder Code dorthin verschieben), dann `uv run
  worker --help`烟雾-testen. Smoke-Test: `worker new-service poc` in einen
  `/tmp`-Worktree erzeugen und löschen.
- 1.4 **Duplikate auflösen** — `worker-platform` als Kernel:
  - `worker-correlation` → reexportiert `worker_platform.context` (oder
    `worker-correlation` wird zum einzigen Ort, platform importiert dann von dort
    — ADR muss die Richtung festlegen; Empfehlung: **Behalte `worker_platform.*
    als Kanon**, da es schon läuft & getestet ist; `worker-*`-Geschwister werden
    zu Dünnschicht-Reexports oder gelöscht, wenn überflüssig.)
  - gleiche Regel für CQRS (`worker-cqrs` ↔ platform) und Health
    (`worker-health` ↔ platform Router).
  - `worker-config`, `worker-tenancy` als **Erweiterungen** von
    `worker_platform.PlatformSettings` definieren, nicht als Konkurrenten.
- 1.5 **pytest-Basis verbreitern** — Smoke-Tests pro geräumtem Paket
  (Import + eine Kernfunktion), nicht Vollabdeckung. Ziel: ≥ 1 Test pro aktives
  Paket.
- 1.6 **Premerge-Sicherung** — in CI evtl. einen `make check`-Wrapper oder
  Dokumentation, dass die 4-Schritt-Reihenfolge bindend ist.
**DoD:** `uv run ruff format --check . && uv run ruff check . && uv run mypy
packages apps && uv run pytest` alles grün. CLI läuft und erzeugt ein
verifizierbares Service-Skelett.

### Phase 2 — Identity & Tenancy (erster vertikaler Slice mit echter Domain)
**Warum:** Auth ist die Voraussetzung für jedes konsenspflichtige Feature
(product-scope.md); liefert als erstes die authentifizierten Claims, die den
Tenant-Kontext ersetzen.
- `apps/identity-service` ausbauen: User-Aggregat, Account-Lifecycle,
  Sessions, OIDC/OAuth2-Einstieg, JWT-Ausgabe/-Verify, Refresh-Tokens.
- `worker-auth` (PyJWT, authlib, passlib) + `worker-authorization` (casbin,
  RBAC/ABAC, Policy-Engine) als Bausteine vom Kernel einbinden.
- PostgreSQL + `worker-database` (SQLAlchemy 2 + Alembic + asyncpg + UoW +
  Repository) als erstes echtes Persistenz-Substrat; Migrations-Workflow
  über `worker migrate/upgrade`.
- Tenant-Kontext von Header-Resolver auf **claims-basiert** umstellen;
  Header-Resolver bleibt nur local/test.
- Audit-Events für sicherheitsrelevante Aktionen (via `worker-events`).
- Frontend-Auth-Flow-Einstieg (Login/Callback) als erste echte Web-Route.
**DoD:** Ein User kann sich anmelden, erhält ein JWT, Tenant kommt aus dem
Claim, Audit-Event persistiert, Identity-Service hat DB-Migration, Tests
für Domain + Integration (Testcontainers). CI grün.

### Phase 2.5 — Stabilisierung & Plattform-Naht ✅ (nachträglich eingeschoben)
**Warum:** Eine Bestandsaufnahme vor Phase 3 fand mehrere Defizite, auf denen
Service #3 sonst aufgebaut hätte — und mehrere Doku-Aussagen, die der Code nicht
deckte. Kein neues Feature; nur die Differenz zwischen Behauptung und Realität.
- Cookie-Auth durchgängig (`GET /me` war aus dem Browser **immer** 401).
- Dependency-Deklarationen repariert + dauerhafter Wächter; `worker-shared` gefüllt.
- Kanon-Auflösung Runde 2 (ADR-0014): `worker-security`/`worker-exceptions` gelöscht,
  `worker-logging` als Reexport.
- `docker-compose.yml` + `.env.example`; der Stack startet später vollständig aus
  Compose heraus (Services migrieren sich selbst, `run-dev.sh` entfiel).
- Frontend-Gate in CI (vorher komplett ungegated); ruff `S` aktiviert.
- Service-Generator repariert — er hat **nie** parsebaren Code erzeugt.
**DoD erfüllt:** `make check` (Python + Frontend) grün; Generator-Ausgabe per Test
abgesichert; alle Funde in ROADMAP dokumentiert.

### Phase 3 — Candidate Core (Profile, Resume, Documents, Consent)
**Stand 01.08.2026:** 3.1 (Consent-Ledger) und 3.1b (Identity-Angleichung +
Onboarding) sind fertig und gemergt — ADR-0013 bis ADR-0019. Registrierung,
E-Mail-Bestätigung, Unternehmensanlage und Tenant-Wechsel laufen über die
Oberfläche; der Stack startet vollständig aus `docker compose up`. Offen:
3.2–3.5 sowie Scheibe C (Einladungen/Rollen).
- `apps/profile-service` — kandidateneigenes Profil, Documents, **Consent
  Ledger** (ohne Consent keine Sichtbarkeit — product-scope.md).
- `apps/resume-service` — CV-Builder, Versionierung, Export, Templates via
  `worker-templates` (Jinja2/WeasyPrint).
- `apps/portfolio-service` — Projekte, verifizierte Skills, Evidence.
- `worker-files` (Upload, Validation, CDN) + `worker-storage` (S3/MinIO)
  einbinden; dokumentieren, dass Credentials/Quellcode nie ins Repo.
- **Consent ist Enabler**, nicht Feature: Jede Sichtbarkeit/Sendung fragt den
  Consent-Ledger.
**DoD:** Profil anlegen, Dokument hochladen, Consent erteilen/entziehen;
Sichtbarkeit respektiert Consent-Ledger. Tests.

### Phase 4 — Jobs & Applications
- `apps/jobs-service` — Job-Posting, Suche, Matching-Vorschläge
  (explainable, kein hidden Employability-Score).
- `apps/applications-service` — Apply, Track, Manage, **AI-assisted
  Bewerbungen als Entwurf** (human review vor Versand), Anhang nur nach
  Freigabe + Empfängerauswahl.
- `apps/companies-service` — Employer-Profile, Team, Culture, Benefits.
- **Career-Site-Connectors** (Greenhouse, Lever, Personio, …) **ohne
  offizielle API nicht gebaut** — ADR je Connector über Source/Permissions/
  Sync/Deletion.
- `apps/career-service` — personalisierte Landingpages, Subdomain+DNS-Route
  (`karriere.firma.de/<bewerber>`), Videos/Benefits/Direktbewerbung.
**DoD:** Job posten, Candidate (mit Consent) bewerben, Bewerbungsstatus
tracken; Landingpage unter Subdomain erreichbar. Tests.

### Phase 5 — Transfermarkt (Differenzierungsmerkmal)
**Warum:** Das, was WorkerTransfer von JobPilot/Portalen unterscheidet.
- `apps/transfer-service` — Marktstatus-State-Machine:
  `Open → Listening → Unavailable → Under Contract → Transfer Listed →
  Negotiating → Transferred`.
- Flow: Interesse → Angebot → Transfergebühr → Bonus → Startdatum →
  Vertragsentwurf → digitale Unterschrift.
- **Beide Wege konsenspflichtig**: beschäftigt → Kontakt + Angebot + Firma muss
  mitwirken; nicht-beschäftigt → direkt Kontakt + Angebot; Ablehnung immer
  möglich.
- `worker-player-advisor`-Agententyp (Transferberater) als AI-Entwurf, niemals
  autonome Verhandlung.
- Contracts-Entwürfe via `worker-templates`; Mensch prüft vor Versand.
**DoD:** State-Machine-Tests, ein voller Happy-Path inkl. Consent-Checks;
Soll-Bruch-Pfade (Ablehnung, Vertragsende) getestet.

### Phase 6 — Developer Intelligence (GitHub-Consent-Skill-Graph)
- `worker-github` (githubkit, httpx, redis) — OAuth, Repo-Scanner,
  Skill-Analyzer, OSS-Reputation, Contribution-Graph.
- `apps/developer-service` — Skill-Graph aus **nur mit Zustimmung** geholten
  öffentlichen Signalen (Commits, PRs, Reviews, Actions, Security Advisories…).
- Mehrdimensionale Scores statt einer Zahl (Technical/Architecture/OSS/
  Community/Leadership/Docs/Testing/DevOps/AI/Security) — explainable.
- AI Developer Scout: natural-language Query → Candidate → CV/GitHub/Skills
  → Wechselwahrscheinlichkeit → Match-Score → Vorschlag (Entwurf).
- AI Portfolio-Generator + AI Code Analyzer **nur mit Einwilligung**.
- MCP-Integration: GitHub als Tool für den Scout.
- Später: GitLab/Bitbucket/StackOverflow/Kaggle/Figma/LinkedIn (je ADR +
  Consent).
**DoD:** Ein Candidate verknüpft GitHub (OAuth, genehmigte Scopes), Skill-Graph
wird gebaut, Scout liefert eine erklärbare Match-Liste. Consent jederzeit
revoke/delete. Tests.

### Phase 7 — AI Agent Plattform
- `worker-ai` (openai/anthropic/gemini/ollama Provider-Abstraktion,
  Tool-Calling, Memory, prompt-Templates, Streaming) + `worker-agents`
  (Planner→Executor→Evaluator→Reflection) + `worker-mcp` (MCP-Client/Server,
  Tool-Registry).
- `apps/ai-service` — Agent Runtime, Memory (kurz/lang/episodisch/semantisch),
  Knowledge + Vector-Search (chromadb/qdrant).
- 23 spezialisierte Agenten aus `kon.txt`, alle **draft-only**:
  - Candidate (11): Career Coach, Bewerbungsexperte, CV Optimizer,
    Anschreiben-Generator, Interview-Trainer, Gehaltsberater, Vertragsberater,
    Transferberater, Skill Analyzer, Learning Coach, Portfolio Builder,
    Dokumentenmanager.
  - Company (10): Scout, Recruiter, Interview, Candidate Ranking,
    Salary Recommendation, Team Analyzer, Talent Discovery, Skill Gap,
    Workforce Planner, Offer Generator.
  - Cross (2): Negotiation Agent (vermittelt), Contract Agent (Entwürfe:
    Arbeitsvertrag/NDA/Aufhebungsvertrag/Transfervertrag/Änderungen,
    immer mit Hinweis auf Rechtsprüfung).
- Agent Marketplace (Drittanbieter-Agenten) nur später, als eigene Phase.
**DoD:** ≥ 1 Candidate- + ≥ 1 Company-Agent läuft als Entwurfs-Erzeuger mit
plan-act-reflect-Schleife, jedes externe Ergebnis fordert Human-Review an.
Consents und Logs vorhanden. Tests.

### Phase 8 — Contracts, E-Signature & Verträge
- `apps/contract-service` — Templates, Generation, **jurisdiktions-spezifische
  Rechtprüfung** vor Produktivbetrieb, E-Signature (DocuSign/Adobe Sign via
  MCP oder Provider-SDK).
- `worker-templates` (Jinja2/WeasyPrint) für Anschreiben/Lebenslauf/CV/
  Verträge.
- Audit-Trail je Vertragsversion.
**DoD:** Vertrag aus Template erzeugen, Review-Pfennig vorhanden, E-Sign
angestoßen, Versionierung + Audit getestet.

### Phase 9 — Messaging, Notifications, Search, Analytics
[parallelisierbar]
- `apps/messaging-service` — Realtime-Chat (WebSocket/SSE) via
  `worker-messaging` (aio-pika/aiokafka/nats) + Outbox/Inbox.
- `apps/notifications-service` — Email/SMS/Push/In-App, Preferences, Templates,
  Queue via `worker-notifications` + `worker-email`.
- `apps/search-service` — Fulltext/Vector/Semantic/Facetten via `worker-search`
  (elastic/meili/qdrant).
- `apps/analytics-service` — Events/Dashboards/Reports, **GDPR-konform**.
- Transactional Outbox/Inbox jetzt schalten, sobald erste
  serviceüberschreitenden Workflows existieren.
**DoD:** Cross-Service-Event fließt via Outbox, Notification geht raus, Suche
findet, Analytics aggregiert datenschutzkonform. Tests (Testcontainers).

### Phase 10 — Frontend, Gateway, Infra, Hardening
- Frontend (`apps/web`) ausbauen: TanStack Router + Query, Zustand/RTK,
  React Hook Form + Zod, Tailwind/Shadcn/Radix, `packages/ui` Design System
  (Buttons/Cards/Dialogs/DataGrid/Forms/Theme/Icons/Charts/Motion),
  Feature-Module je Domain, API-Mock ts aus OpenAPI generiert.
- `apps/gateway` (Traefik/Envoy) — Auth/Routing/Correlation/Metrics/OTel,
  **keine Businesslogik**.
- Docker, K8s/Helm, GitOps (ArgoCD/Flux), Observability-Stack (OTel Collector
  → Jaeger/Prometheus/Loki + Grafana + Alerting + SLO/SLI).
- Production-Hardening: Verschlüsselung, DPIA-Werte, Retention/Deletion/Export,
  Access-Control, Threat-Modell, Dependency-Scanning.
  - **Per-IP Rate-Limiting am Auth-Rand (Phase-2-Folge):** `worker-ratelimit`
    (Token-Bucket, implementiert) an `POST /auth/login` und `/auth/refresh` im
    identity-service anbinden *bevor* das Gateway externen Traffic zulässt.
    Phase-2 hat bewusst nur den TODO-Marker in
    `apps/identity-service/src/identity_service/presentation/http/router.py`
    (`build_auth_router`) gesetzt; der Service lauscht bis dahin loopback und
    ist nicht extern erreichbar — der Marker ist in Phase 10 einzulösen.
**DoD:** Frontend baut&typecheckt, Gateway routet, Docker-Compose bringt den
Stack hoch, OTel sichtbar, Hardening-Checkliste abgehakt. CI grün.

---

## Skills (`.opencode/skill/`)

Vorhanden: `clean-architecture`, `ai-agents`, `database-layer`, `auth-authz`,
`frontend`, `python-monorepo`, `devops`.

Neu/ergänzen:
- `worker-cli.md` — Service/Package/Domain-Scaffold + when to use CLI vs manual.
- `consent-ledger.md` — Consent als Architekturmuster (Enabler, nicht Feature).
- `transfer-market.md` — State-Machine, Konsens-Flows, Vertragstitel,
  Ablehnungspfade.
- `developer-intelligence.md` — GitHub OAuth Consent, Skill-Graph,
  mehrdimensionale Scores, Scout-Match-Pipeline.
- `contracts-and-signing.md` — Template-Generation, jurisdiktions-Pflicht-Review,
  E-Sign.
- `eventing-outbox.md` — Outbox/Inbox + Integration-Events vs Domain-Events.
- `mcp-integration.md` — Provider-Registry, Tool-Registry,
  Consent-Voraussetzung.

Jeder Skill: Zweck, Wann-verwenden, konkrete Patterns mit Code-Snippets,
Verweise auf die zugehörigen `worker-*`-Pakete und ADRs.

## Repo-Disziplin
- **Verbindliche Check-Reihenfolge:** ruff format → ruff check → mypy packages
  apps → pytest → (frontend) pnpm check → pnpm test.
- **Commit pro Schritt** in den Phasen 4+; Phasen 1–2 eher pro Paket.
- **ADR** je architektonische Entscheidung in `docs/adr/`.
- **Kein fluent `PlatformBuilder`** — Composition-Root (ADR-0003).
- **Skills** als Wissensspeicher, nicht als细则, leben in `.opencode/skill/`.

## Risiko-Register
1. **Scope-Drift durch kon.txt-Breite.** ← Stufenweiser Plan, DoD pro Phase,
   Marketplace erst post-Phase-7.
2. **CI fällt wieder rot** nach neuen Implementierungen, weil mypy-strict.
   ← Phase-1-1.5 Smoke-Tests + Premerge-Sicherung; kein Merge ohne grüne CI.
3. **Consent-Vergessen in Performance-Rollen.** ← consent-ledger-Skill +
   Tests, die Consent-Entzug=enforce-Sichtbarkeit prüfen.
4. **Scraping-Lockerversuchung** bei Career-/Profile-Quellen. ← ADR-0004 +
   Konnektor-ADR je Quelle; Skill `developer-intelligence` macht dies explizit.
5. **AI-Autonomie-Drift** (Ranking/Ablehnung/Kontakt). ← product-scope-Skill
   strapaziert "AI entwirft, Mensch entscheidet" in jedem AI-Task.
6. **Architektur-Duplikat-Rückfall.** ← Phase 1.4 entscheidet Kanon pro
   Konzept; ADR-0002 verbietet Re-Duplikation.
7. **Heavy-Dep-Bloat** (294 Pakete). ← In Phase 1.2/1.4 prüfen: deps declared
   but unused → raus. Build- & Testzeit.

## Status & Fortschirtt
Geführt in `docs/ROADMAP.md` (ein Status pro Phase + Sub-Punkt).
Beim Eintritt in eine Phase → `TaskCreate` pro Sub-Punkt; beim Abschluss →
`TaskUpdate completed` + Roadmap-Eintrag.
