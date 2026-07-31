# Roadmap

Pull-Through-Index zu [ULTRAPLAN.md](./ULTRAPLAN.md). Status wird hier
gepflegt; Sub-Punkte werden bei Eintritt in eine Phase als Tasks angelegt.

Legende: ⬜ nicht begonnen · 🟧 in Arbeit · ✅ erledigt · ⛔ blockiert

## Phasen

| # | Phase | Status | DoD-Kurz |
|---|-------|--------|----------|
| 0 | Repo-Disziplin & Betriebsamskeit | ✅ | Roadmap, ADRs, Skills, Glossar |
| 1 | Foundation festigen (CI grün) | ✅ | ruff 0 ✅, mypy 0 ✅, CLI-Entrypoint ✅, Duplikate ✅, Smoke-Tests ✅, Premerge-Wrapper ✅ |
| 2 | Identity & Tenancy | ✅ | OIDC/OAuth, JWT, Claims-Tenant, Audit, DB-Migration |
| 2.5 | Stabilisierung & Plattform-Naht | ✅ | Cookie-Auth, Dep-Hygiene, Kanon Runde 2, Dev-Stack, Frontend-Gate, Generator |
| 3 | Candidate Core | 🟧 | Profile/Resume/Portfolio + Consent-Ledger (3.1 in Arbeit) |
| 4 | Jobs & Applications | ⬜ | Jobs, Applications, Companies, Career-Sites |
| 5 | Transfermarkt | ⬜ | Market-State-Machine, Konsensflows, Vertragsdraft |
| 6 | Developer Intelligence | ⬜ | GitHub-Consent, Skill-Graph, Scout-Match |
| 7 | AI Agent Plattform | ⬜ | 23 Agenten (draft-only), plan-act-reflect, MCP |
| 8 | Contracts & E-Signature | ⬜ | Templates, Rechtsprüfung, E-Sign, Audit |
| 9 | Messaging/Notif./Search/Analytics | ⬜ | Outbox/Inbox, cross-service Events |
| 10 | Frontend/Gateway/Infra/Hardening | ⬜ | UI, Gateway, K8s, OTel, Hardening |

### Phase 0 — Status: ✅ erledigt
- ✅ `docs/ULTRAPLAN.md` + `docs/ROADMAP.md`
- ✅ ADR-0002 Kernel-vs-Bausteine
- ✅ ADR-0003 Composition-Root statt fluent PlatformBuilder
- ✅ ADR-0004 Vertragsmodell / kein Scraping / Consent-First
- ✅ `AGENTS.md` / `CLAUDE.md` / `CONTRIBUTING.md` Ultraplan-Verweise
- ✅ `docs/skills/` angelegt: worker-cli, consent-ledger, transfer-market
  (neue Skills skills unter `docs/skills/`, da `.opencode/skill/` root-owned
  ist; die bestehende `.opencode/skill/`-Dateien bleiben unangetastet).
- ✅ `docs/glossary.md`

### Phase 1 — Status: ✅ erledigt (1.1 ✅, 1.2 ✅, 1.3 ✅, 1.4 ✅, 1.5 ✅, 1.6 ✅)
- ✅ 1.1 ruff aufräumen (F401/RUF013/ASYNC/UP…) → 0
  (`ruff format --check .` + `ruff check .` beide 0 Fehler; 12.07.2026)
- ✅ 1.2 mypy grün (scheduler, ai, resilience, validation, messaging, …) → 0
  (Stand 15.07.2026: 311 → 0 in 54 Quelldateien. Alle ~28 Pakete + apps typisiert.
  Mittel: `dict`→`dict[str,Any]`, `tuple`→`tuple[Any,...]`, `Callable[...,Any]`,
  `no-any-return` via str()/int()/bool()-Cast, ungetypte async-Clients via
  `cast("Any",…)`, echte Bugs korrigiert (search `create_index`-Indentation,
  contracts `class DomainEvent(DomainEvent)`-Reexport, github Param-Shadowing,
  files `generate_presigned_url`→`get_presigned_url`+size-Bug, worker-tenancy
  `NoTenantResolver` ergänzt). `py.typed`-Marker in allen 37 Paketen;
  mypy-overrides für 19 externe ungetypte Libs. `uv run mypy packages apps`=0.)
- ✅ 1.3 CLI reparieren (`worker_cli.main`), smoke-testen
  (15.07.2026. `packages/worker-cli/src/worker_cli/main.py` re-exportiert das
  Typer `app`; Entry `worker_cli.main:app` löst jetzt. `new-package` erzeugt ein
  gültiges Paket. Sub-Befehle command/query/entity/event/consumer/publisher nutzen
  leere `cqrs`/`domain`/`infrastructure`-Templates = Folge-Defizit.
  **Korrektur 31.07.2026 (Phase 2.5/A6):** die damalige Aussage „`new-service`
  erzeugt vollständiges Clean-Arch-Skelett" war **falsch**. Der Smoke-Test prüfte
  nur `--help`; die erzeugte Ausgabe hat nie jemand kompiliert. Sie enthielt
  wörtliche `${ServiceClass}`-Platzhalter und verdrahtete die nicht existierende
  `PlatformBuilder`-API — 45 ruff-Fehler, überwiegend `invalid-syntax`. Repariert
  und per Test abgesichert in Phase 2.5.)
- ✅ 1.4 Duplikate auflösen (correlation, cqrs, health, config, tenancy) — Kanon
  = worker-platform; ADR-0002
  (15.07.2026. Kanon-Auflösung nach [ADR-0005](adr/0005-canon-resolution-duplicates.md).
  `worker-cqrs` gelöscht (byte-identisch mit `worker_platform.application.cqrs`,
  0 Importeure) — Verzeichnis entfernt, `uv.lock`-/`[tool.uv.sources]`-Einträge
  losgelöst. `worker-correlation` → Dünnschicht-Reexport von
  `worker_platform.context` (alle 6 Symbole per `is` identisch mit dem Kanon);
  einziger Konsument `worker-logging` läuft unverändert weiter. `worker-config` →
  Reexport der `PlatformSettings`-Familie (`PlatformSettings`, `Environment`,
  `BaseSettings`, `SettingsConfigDict` aus `worker_platform.configuration`);
  `pyproject` hängt nun direkt am Kanon, verwaiste `worker-core`/`worker-shared`
  source-Decl entfernt. `worker-health` + `worker-tenancy` als ergänzende Bausteine
  belassen (Router in platform, konkrete Dependency-Checks in worker-health;
  Tenant-Resolver in worker-tenancy) — kein inhaltliches Duplikat. Nebenbefund:
  `normalize_correlation_id` hatte einen `except AttributeError, ValueError:`-Bug
  (gültige Syntax, aber fängt nur eine Exception & bindet Instanz an den anderen
  Namen; `UUID(<ungültig>)`-`ValueError` wurde nicht gefangen → Crash statt
  neue ID; ruff/mypy sahen nichts) — im Kanon zu `except ValueError:` korrigiert.
  Phase-1-Gates danach grün: ruff format 57 clean, ruff check 0, mypy 54 Quellen
  0 Issues, pytest 7 passed. Folge-Defizite dokumentiert: worker-tenancy führt
  eigene `_tenant_id`-ContextVar (UUID-Typ) zusätzlich zum Kanon (str-Typ) →
  Tenant-Context-Konsolidierung auf Phase 2 verschoben.)
- ✅ 1.5 Smoke-Tests pro aktives Paket
  (15.07.2026. 34 Smoke-Test-Dateien + 58 Smoke-Test-Funktionen neu; pytest
  gesamt 7 → 65 (63 passed, 2 skipped). Jedes aktive `worker-*`-Paket mit
  echter Implementierung hat nun `tests/test_smoke_<pkg>.py` (worker-shared
  ausgenommen — leere `__init__.py`, keine Oberfläche; worker-platform hatte
  schon Tests). Konvention: Import
  gelingt + eine reine Kernfunktion/ein trivialer Konstruktor ohne externe
  Dienste (kein Netz/DB/Redis/FS-Write/Model-Download); async via
  `asyncio_mode="auto"`.) Entscheid je Paket: reiner Konstruktor (worker-ai,
  worker-agents, worker-mcp, worker-database, worker-cache, worker-search,
  worker-messaging, worker-notifications, worker-email, worker-resilience,
  worker-templates, worker-validation, worker-auth, worker-core, worker-health,
  worker-exceptions, worker-contracts, worker-events, worker-metrics,
  worker-correlation, worker-config, worker-logging, worker-middleware,
  worker-tenancy, worker-cli via `typer.testing.CliRunner`-`--help`); reine
  Funktion (worker-exceptions `to_problem_detail`, worker-events `to_dict`,
  worker-core `Result`); module-import-only (worker-storage, worker-ratelimit,
  worker-scheduler, worker-security, worker-authorization, worker-telemetry,
  worker-tracing — nur Heavy-Client/ASGI-App-Oberfläche). 2x
  `pytest.skip` mit Grund: worker-github (`from github import Github` schlägt
  fehl — Declared-Dep ist `githubkit`, Source nutzt PyGithub = Mismatch) und
  worker-files (`import magic` schlägt fehl — System-`libmagic` fehlt).
  Collection-Hürde gelöst: alle Smoke-Dateien heißen eindeutig
  `test_smoke_<pkg_unterstrichen>.py` (nicht nur `test_smoke.py`), weil pytest
  ohne `tests/__init__.py` und rootdir-Importmode sonst
  Basisnamens-Kollisionen wirft; Unterstrich statt Bindestrich, da Python-
  Modulnamen keine Bindestriche dürfen. Phase-1-Gates danach grün: ruff format
  91 clean, ruff check 0, mypy 54 Quellen 0 Issues, pytest 63 passed + 2
  skipped.) Folge-Defizite: (a) worker-exceptions nutzt veraltete Konstante
  `HTTP_422_UNPROCESSABLE_ENTITY` (Starlette heißt sie jetzt
  `HTTP_422_UNPROCESSABLE_CONTENT`) → DeprecationWarning, von neuem Smoke
  aufgedeckt; Korrektur ist Source-Change jenseits 1.5. (b) worker-github
  Dep-Mismatch, worker-files libmagic — beide blockieren Smoke jenseits skip.)
- ✅ 1.6 Premerge-Sicherung (Doku/Wrapper)
  (16.07.2026. `Makefile` im Repo-Root mit `make check` (= ruff format --check
  → ruff check → mypy → pytest, fail-fast), `make fix` (ruff format +
  ruff check --fix), `make lint`/`make type`/`make test`/`make sync`/`make ci`/
  `make clean` + `make help`. Jeder Python-Befehl läuft durch `uv run` — kein
  pip/poetry. CI (`.github/workflows/ci.yml`) auf dieselbe Reihenfolge
  synchronisiert (vorher pytest-vor-mypy — inkonsistent mit der bindenden
  Doku-Reihenfolge; jetzt ruff format → ruff check → mypy → pytest wie `make
  check` und `AGENTS.md`/`CLAUDE.md`). Doku verdrahtet: `AGENTS.md` (drei
  Stellen), `CONTRIBUTING.md`, `README.md`, `CLAUDE.md` verweisen auf `make
  check`/`make fix` als Einzeiler und behalten die expliziten uv-Schritte als
  Äquivalent. Verifiziert: `make check` = Exit 0 (63 passed + 2 skipped),
  `make lint`/`make type`/`make test` alle Exit 0, `make fix` idempotent — Phase 1
  DoD erfüllt.)

### Phase 2 — Status: ✅ erledigt (2.1–2.9 ✅)
- ✅ 2.1 worker-auth repariert (bcrypt direkt + HS256 via PyJWT, jose gefallen) — ADR-0006/0007
- ✅ 2.2 pro-service async Alembic + worker-cli-Reparatur — ADR-0010
- ✅ 2.3 identity-domain (User-Aggregat, Email/PasswordHash/UserId/TenantId Value Objects,
  AccountStatus synchron-ACTIVE, AuditEvent, Ports)
- ✅ 2.4 Persistenz + Migration `0001_init_users_sessions_audit` + Testcontainers — ADR-0011
- ✅ 2.5 application commands (register/login/refresh) + HTTP `/auth/*` + `/me` + auth middleware
- ✅ 2.6 tenant consolidation (worker-tenancy re-export + ClaimTenantResolver aus JWT-Claim;
  `X-Tenant-ID` in prod ignoriert) — ADR-0009
- ✅ 2.7 audit EventBus-Seam + atomicity-Tests (login_success/login_failure persistiert,
  `actor_id` NULL bei unbekanntem User) — ADR-0012
- ✅ 2.8 frontend `/login` (deutsch, TanStack Router code-router, cookie-auth);
  worker-platform optional CORS hook (dev-allowlist, prod-refused) als cookie-auth-enabler
- ✅ 2.9 CI bereit für Testcontainers (Docker auf `ubuntu-latest`, offline-skip ADR-0011) + ROADMAP ✅
- ADRs geschrieben: 0006, 0007, 0008, 0009, 0010, 0011, 0012
- DoD erfüllt: User kann sich anmelden → `POST /auth/login` 200 + `access`/`refresh` HTTP-only-Cookies
  (`test_auth_endpoints.py`); erhält JWT (HS256, `worker-auth`); Tenant kommt aus dem Claim
  (`/me`, `test_tenant_source.py`); Audit persistiert (`test_audit_atomicity.py`);
  DB-Migration läuft (`test_migrations.py`); Testcontainers-Integration; frontend `/login`;
  `make check` + `pnpm check`/`pnpm test` grün.
- Folge-Defizit (nicht blockierend): per-IP-Rate-Limiting fehlt — TODO-Marker in
  `build_auth_router` (Phase-10, `worker-ratelimit`).

### Phase 2.5 — Status: ✅ erledigt (31.07.2026)

Nicht im ursprünglichen ULTRAPLAN vorgesehen. Eingeschoben, weil eine Bestandsaufnahme
vor Phase 3 mehrere Fundament-Defizite fand, auf denen Service #3 sonst aufgebaut hätte.
Kein neues Feature — nur die Differenz zwischen dem, was die Docs behaupteten, und dem,
was der Code tat.

- ✅ **A1 Cookie-Auth durchgängig.** `AuthMiddleware` las **nur** `Authorization: Bearer`,
  `POST /auth/login` liefert das Token aber als `httpOnly`-Cookie und `apps/web` sendet
  keinen Header → `GET /me` gab aus dem Browser **immer 401**. Die Phase-2-DoD
  („Frontend `/login`") war insoweit überzeichnet: der Flow war nie durchgängig.
  Cookie-Fallback ergänzt, 7 Unit-Tests (ohne Docker) + Integrationspfad. Frontend
  konsumiert `fetchMe` jetzt über `useSession`; vorher war TanStack Query zwar
  eingebunden, aber **nirgends benutzt** — die App kannte keinen Login-Zustand.
- ✅ **A2 Dependency-Hygiene.** 7 Pakete importierten Geschwister, die sie nicht
  deklarierten (`worker-contracts`→`worker-core`, `worker-correlation`→`worker-platform`,
  `worker-telemetry`→3, …). Funktionierte nur, weil `uv sync --all-packages` alles in
  *ein* venv flacht; einzeln installiert wäre jeder Service gebrochen.
  `tests/test_workspace_dependencies.py` hält es fixiert (verifiziert: schlägt an,
  wenn man die Deklaration wieder entfernt). `worker-shared` war eine **0-Byte-Datei**,
  von der beide Services abhingen — enthält jetzt `utc_now`/`Page`/`Cursor`/`Money`
  (19 Tests, stdlib-only).
- ✅ **A3 Kanon-Auflösung Runde 2** — [ADR-0014](adr/0014-kernel-duplicates-round-2.md).
  ADR-0005 hatte nur Pakete *mit* Importeuren betrachtet; vier weitere duplizierten den
  Kernel. `worker-exceptions` exportierte eine **gleichnamige** Funktion mit
  abweichender Problem-Shape (kein `correlationId`, `str(exc)` im `detail`) →
  gelöscht. `worker-security` (falsche Middleware-Basisklasse) → gelöscht.
  `worker-logging` → Reexport; sein `configure_logging` hängte bei jedem Aufruf einen
  neuen Handler an (jede Logzeile doppelt). packages/ 36 → 34.
- ✅ **A4 Dev-Umgebung.** `docker-compose.yml` (Postgres, eine DB pro Service),
  `scripts/initdb/`, `.env.example`. `run-dev.sh` migriert jetzt vor dem Start und
  startet beide Services. **Bugfix:** das Skript setzte `IDENTITY_JWT_SECRET` —
  `PlatformSettings` liest `env_prefix="WORKER_"`, die Variable wurde also ignoriert
  (verifiziert: `applied? False`). Korrekt ist `WORKER_JWT_SECRET`.
- ✅ **A5 CI ehrlich.** Es gab genau **einen** Python-Job — `apps/web` und `packages/ui`
  waren komplett ungegated, TS-Fehler mergten grün. Neuer `frontend-quality`-Job
  (install/check/test/build, Node 24); `make check` deckt jetzt alle sechs
  AGENTS.md-Schritte ab. `packages/ui` hatte **kein** `test`-Script (turbo lief dort
  ins Leere) → 12 Tests. ruff `S` aktiviert; der vorhandene `S101`-Ignore war
  mangels `S` in `select` toter Konfigurationstext. 14 Befunde einzeln bewertet:
  3 echte Fixes (2× stilles `except: pass`, `/tmp/storage`-Default), Rest begründet
  annotiert.
- ✅ **A6 Service-Generator** — kon.txt „Regel Nr. 1". `worker new-service` hat **nie**
  lauffähigen Code erzeugt: Templates verlangten `${ServiceClass}`, der Kontext lieferte
  `service_class` → `safe_substitute` schrieb den Platzhalter wörtlich in die Ausgabe
  (45 ruff-Fehler, meist `invalid-syntax`). Zusätzlich verdrahteten sie
  `worker_platform.builder.PlatformBuilder` — die von **ADR-0003 verworfene** und nicht
  existierende API. Phase 1.3 hatte nur `--help` smoke-getestet; die Ausgabe hat nie
  jemand kompiliert. Templates jetzt Spiegel von `identity-service` (Composition-Root
  via `create_api_app`, Alembic ADR-0010, Testcontainers ADR-0011); der Renderer
  verweigert `.py`-Dateien mit ungelösten Platzhaltern. 10 Tests.

**Offene Folge-Defizite aus 2.5** (dokumentiert, nicht blockierend):
- Die Alt-Templates `domain/`, `application/`, `infrastructure/` im Generator tragen
  eigene Lint-Schulden und definieren einen **lokalen** `Mediator`/`PipelineBehavior`,
  der `worker_platform.application.cqrs` dupliziert — ein ADR-0002-Verstoß, der sonst in
  jeden generierten Service eingebacken wird.
- `worker-github` bleibt unimportierbar (Source nutzt PyGithub, deklariert ist
  `githubkit`). Wird erst in Phase 6 gebraucht; Fix gehört dorthin.
- `worker-ai`/`worker-files` bleiben aus dem Workspace exkludiert (ML-/C-Wheels ohne
  Python-3.14-Rad). Für Phase 7 über optionale Extras zu lösen, analog weasyprint.
- Kein `Authorization`-Header **und** kein Cookie ⇒ 401; per-IP-Rate-Limiting am
  Auth-Rand weiterhin offen (Phase 10, TODO-Marker in `build_auth_router`).

### Phase 3 — Status: 🟧 in Arbeit

- 🟧 **3.1 Consent-Ledger** (`apps/consent-service`) — Spec und Plan liegen vor
  ([Design](superpowers/specs/2026-07-26-phase-3-substep-3.1-consent-ledger-design.md),
  [Plan](superpowers/plans/2026-07-29-phase-3-substep-3.1-consent-ledger.md)).
  Fertig: Workspace-Scaffold, Alembic-Gerüst, Domain-Value-Objects
  (`SubjectId`, `Capability`, `ConsentEventId`, `Reason`, `ConsentAction`).
  Offen: `ConsentEvent`-Aggregat, `project_state`, Ports, Migration `0001`,
  Repositories, Commands/Mediator, Composition-Root, HTTP-Router — und
  `presentation/compose_api.py` ist noch ein Platzhalter, der eine nackte `FastAPI()`
  baut und `create_api_app` umgeht (keine Correlation-ID, keine Security-Header,
  keine Problem-Details). ADR-0013 noch nicht geschrieben.
  Zwei in der Spec offene Entscheidungen sind im Phase-2.5-Plan getroffen:
  JWT-Verifikation gehört als Baustein nach `worker-auth` (nicht cross-service
  importiert — ADR-0002/0004), und das geteilte HS256-Secret ist als
  Trust-Domain-Annahme zu dokumentieren (→ ADR-0015).
- ⬜ 3.2 Profile-Service · ⬜ 3.3 Resume-Service · ⬜ 3.4 Portfolio-Service
- ⬜ 3.5 `worker-files`/`worker-storage` real machen (Workspace-Re-Include)

Nächste Aktion: **Sub-step 3.1 fertigstellen.** Der Consent-Ledger ist der Enabler für
jede Sichtbarkeit in den Phasen 3–6; ohne ihn kann Profile/Resume/Portfolio nicht
consent-konform gebaut werden.