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
| 3 | Candidate Core | 🟧 | Consent-Ledger ✅ (3.1); Profile ✅ (3.2); Resume ✅ (3.3); Portfolio offen |
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
  `scripts/initdb/`, `.env.example`. (Später ersetzt: der Stack startet vollständig
  aus Compose, jeder Service-Container migriert sich selbst, `run-dev.sh` entfiel.) **Bugfix:** das Skript setzte `IDENTITY_JWT_SECRET` —
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

- ✅ **3.1 Consent-Ledger** (`apps/consent-service`) — 31.07.2026.
  [Design](superpowers/specs/2026-07-26-phase-3-substep-3.1-consent-ledger-design.md) ·
  [ADR-0013](adr/0013-consent-ledger-standalone.md) ·
  [ADR-0015](adr/0015-shared-jwt-middleware.md) ·
  [ADR-0016](adr/0016-per-service-declarative-base.md)

  Append-only Ledger mit `POST /consent/{grant,revoke,delete,check}`.
  Kernpunkte:
  - **Append-only ist strukturell**, nicht per Konvention: `ConsentEvent` ist
    `frozen`, das Repository bietet weder `update` noch `delete` — ein Test prüft
    die Abwesenheit dieser Methoden.
  - **Kein Status-Tisch.** `project_state()` (reine Funktion) definiert die Regeln,
    `latest_effective()` führt dieselbe Reduktion als `DISTINCT ON` aus. Ein Ort,
    an dem Consent wahr ist.
  - **Revocation wirkt sofort** — kein Cache (Ansatz C der Spec wurde genau deshalb
    verworfen). Integrationstest über HTTP: grant → check `true` → revoke → check
    `false`.
  - **Audit atomar** in derselben UoW (ADR-0012); Allowlist lässt den
    Capability-Namen zu, nie die Daten dahinter (PII-Test über die Rohspalte).
  - **`compose_api.py` läuft jetzt durch `create_api_app`** — der Platzhalter mit
    nackter `FastAPI()` ist weg; `test_app.py` prüft Correlation-ID,
    Security-Header und RFC-9457, damit er nicht zurückkommt.
  - Selbstverwaltung: `actor_id == subject_id` erzwungen (403 sonst); `/check` steht
    jedem authentifizierten Aufruf offen, weil genau das den Enabler ausmacht.

  **Zwei Funde beim Bauen**, beide als ADR festgehalten:
  - *ADR-0015*: die JWT-Middleware wandert nach `worker-auth`, statt kopiert zu
    werden — eine Kopie hätte den gerade behobenen Header-only-Bug reproduziert.
  - *ADR-0016*: `worker_database.Base` ist **eine** MetaData. identity- und
    consent-service besitzen beide (korrekt) eine Tabelle `audit_events` →
    `InvalidRequestError` bei der Test-Collection, und Autogenerate hätte die
    Tabellen des jeweils anderen Service als löschbar gesehen. ADR-0010s Annahme
    „Model-Import-Disziplin" war ein Topologie-Problem. Jeder Service hat jetzt
    seine eigene `DeclarativeBase`.

  Offen aus 3.1 (nicht blockierend): `mediator.py` wurde nicht gebaut — die
  Handler werden wie in `identity-service` direkt aus dem Router aufgerufen; ein
  Mediator lohnt erst mit Pipeline-Behaviors. `test_eventbus_seam.py` fehlt (die
  Naht existiert in `compose_infrastructure`, hat aber noch keinen Publisher).
  Die `worker new-service`-Templates erzeugen noch `worker_database.Base` statt
  einer service-eigenen Base (ADR-0016-Folgearbeit).
- ✅ **3.1b Identity-Angleichung & Onboarding** (01.08.2026, PR #2 + #3 gemergt).
  Tenant ist ein Unternehmen, natürliche Personen haben keinen (**ADR-0017**);
  Mitgliedschaft als eigene Relation plus verifizierter Wechsel über
  `POST /auth/company/{id}` (**ADR-0018**); Registrierung, E-Mail-Bestätigung und
  Unternehmensanlage über die abgeleitete Firmendomain (**ADR-0019**).
  Migrationen `0002`/`0003`. Mailpit im Compose-Stack; der ganze Weg von der
  Registrierung bis zum Tenant-Wechsel ist integrationsgetestet **ohne einen
  einzigen SQL-INSERT**. Frontend: `/register`, `/verify`, `/company/new`, eine
  konsolidierte Kopfzeile und `Field` als erstes Formular-Primitiv im
  Designsystem.
  **Unterwegs gefunden und behoben:** die Freischaltung wurde nie gespeichert
  (losgelöstes Aggregat ohne `save()`); der Mailversand lief in der offenen
  Transaktion; die Doppelregistrierung war an der Antwortzeit erkennbar;
  Abmelden ließ das Access-Cookie stehen und beendete die Sitzung nicht.
- ✅ **3.2 Profile-Service** (`apps/profile-service`) — 02.08.2026.
  [Design](superpowers/specs/2026-08-01-profile-service-design.md) · **ADR-0020**.
  Aus `worker new-service` erzeugt, nicht kopiert — und damit zugleich der erste
  echte Test des Generators (siehe unten).
  Ein Profil je Person (`subject_id` IST der Schlüssel), Cursor-Pagination über
  `(updated_at, subject_id)`, und **keine Sichtbarkeit im Aggregat**: die steht
  ausschließlich im Ledger. Vier Endpunkte, deren Statuscodes den Entwurf tragen —
  `404` für „verborgen ODER nicht vorhanden" (bis auf die Korrelations-ID
  byteweise dieselbe Antwort), `403` für „kein aktives Unternehmen" (Aussage über
  den Aufrufer, nicht über das Ziel), `503` wenn der Ledger schweigt.
  **Die Sofortwirkung ist belegt, nicht behauptet:**
  `tests/integration/test_consent_gated_reads.py` fährt Consent- und
  Profile-Service mit je eigener Datenbank hoch und prüft anlegen → freigeben →
  `200` → widerrufen → `404` ohne Wartezeit dazwischen.
  Frontend: `/profile` (bearbeiten + Freigabeschalter) und `/candidates`
  (Kandidatenliste, nur mit aktivem Unternehmen), testgetrieben; `Switch` und
  `TextArea` neu im Designsystem. Dazu eine Playwright-Reise durch den Browser
  über alle drei Dienste und `scripts/validate.sh` als ein Befehl, der alles
  prüft, durchläuft statt abzubrechen und übersprungene Tests benennt.
  **Unterwegs gefunden und behoben:** der Router las `principal.user_id`, der
  Prinzipal heißt `TokenPayload.sub` — `PUT /profiles/me` war für jeden
  angemeldeten Aufrufer kaputt, und 41 Unit-Tests konnten das nicht sehen, weil
  keiner den Router je über die echte Middleware aufrief. `get_request_user` sitzt
  jetzt in `worker-auth` statt in drei Kopien. Zwei Betriebsfallen im Compose:
  `scripts/initdb` läuft nur bei leerem Volume, und der Web-Container startet
  nicht mehr, sobald sich die Lockfile geändert hat (pnpm ohne TTY).
- ✅ **3.3 Resume-Service** (`apps/resume-service`) — 02.08.2026.
  [Design](superpowers/specs/2026-08-02-resume-service-design.md).
  Der Lebenslauf wird **nicht veröffentlicht**: ein Unternehmen fragt, die Person
  antwortet, und die Freigabe gilt genau diesem einen Unternehmen — als
  Capability `resume.visibility:tenant:<uuid>` im Ledger. Am Consent-Service war
  dafür nichts zu ändern; sein Capability-Muster erlaubt das dritte Segment
  bereits.
  Die tragende Trennung: **der Vorgang ist keine Berechtigung.** `GRANTED` heißt
  „wurde einmal erteilt", nicht „gilt gerade" — `ResumeRequest` hat deshalb weder
  `is_active` noch `revoked_at`. Nach einem Widerruf bleibt der Vorgang `GRANTED`
  und der Lesezugriff läuft trotzdem ins Leere.
  Domäne monatsgenau statt taggenau, `ended_on = None` heißt „läuft noch", genau
  eine offene Station, Reihenfolge aus den Daten statt aus einer `sort_order`.
  „Einmal fragen" steht als Unique-Index in der Datenbank, nicht nur im Handler.
  Frontend: `/resume` (bearbeiten + eingegangene Anfragen) und „Lebenslauf
  anfragen" je Kandidatenkarte, testgetrieben; dazu eine zweite Playwright-Reise.
  **Unterwegs gefunden:** der Ersatzwert in `env.ts` zeigte auf `127.0.0.1`,
  während die Seite auf `localhost` läuft — Cookies behandeln das als
  verschiedene Hosts. Eine fehlende `VITE_*`-Variable erzeugte damit keinen
  Verbindungsfehler, sondern Anfragen ohne Sitzungscookie, die wortlos mit 401
  antworten. Der Ersatz nimmt jetzt den Host der Seite. Zweitens: `docker compose
  restart` übernimmt neue Umgebungsvariablen nicht — dafür braucht es
  `docker compose up -d`.
- ⬜ 3.4 Portfolio-Service
- ⬜ 3.5 `worker-files`/`worker-storage` real machen (Workspace-Re-Include)
- ✅ **Scheibe C — Einladungen & Rollen** (02.08.2026). Ein Administrator lädt
  eine **Adresse** ein (nicht ein Konto: die Person muss noch keines haben, und
  beim Einladen darf nicht verraten werden, ob sie eines hat). Angenommen wird
  nur mit Token **und** passender Adresse — Tokens werden weitergeleitet, und
  wer den Link hat, ist nicht, wer eingeladen wurde. Der Token steht weder in
  der Antwort noch in der Liste offener Einladungen und in der Datenbank nur als
  Hash. Erneutes Einladen ersetzt die offene Einladung (Teilindex auf
  `status = 'pending'`), sonst hätte ein Rückzug einen noch gültigen Zwilling.
  `admin` gegen `member` wird jetzt durchgesetzt: nur Administratoren laden ein
  und ziehen zurück, Mitglieder sehen die Mannschaft. Die Firmendomain spielt
  dabei bewusst keine Rolle — sie beweist, wem die Domain gehört (ADR-0019); wen
  das Unternehmen danach hereinlässt, ist seine Entscheidung.
  **Offen:** ein Mitglied wieder zu entfernen. Der gefährliche Teil davon —
  dass ein entzogener Zugang beim Refresh wirkt — ist bereits erledigt.
  ✅ **Die Mitgliedschaftsprüfung in `handle_refresh` ist vorgezogen und erledigt**
  (02.08.2026). Sie war als Folgearbeit zum Entfernungspfad notiert, wurde aber
  vorher gebaut: so ist der Entfernungspfad vom ersten Tag an sicher, statt eine
  Lücke zu öffnen, die man danach schließen müsste. Ein entzogener Zugang wirkt
  jetzt beim nächsten Refresh; die Sitzung überlebt und die Person handelt wieder
  als Person. Nebenbei repariert: `_FakeTokens` gab in den Unit-Tests immer
  `tenant_id=None` zurück und konnte über den Tenant im Refresh deshalb gar
  nichts aussagen.

Nächste Aktion: **Sub-step 3.4 — Portfolio-Service.** Profil und Lebenslauf
decken ab, wer jemand ist und wo er war; das Portfolio zeigt, was dabei
entstanden ist. Der Weg ist derselbe wie bei 3.2 und 3.3: `worker new-service`,
Consent vor jedem fremden Zugriff, Integrationstest gegen echte Dienste. Ob die
Freigabe wie beim Profil öffentlich oder wie beim Lebenslauf je Unternehmen
läuft, ist die erste Entwurfsfrage — beide Formen stehen als Muster bereit.

Teilweise geschlossen: Eine offene Anfrage zeigt die Kopfzeile jetzt als Zähler
am Lebenslauf-Link, damit der Anfragefluss nicht ins Leere läuft. **Eine Mail
gibt es weiterhin nicht** — der Weg existiert (identity-service, Mailpit), aber
Benachrichtigungen sind ein Querschnittsthema mit eigenen Einstellungen und
eigenem Consent und gehören nicht nebenbei in einen Fachschnitt. Wer sich nicht
anmeldet, erfährt von einer Anfrage also nach wie vor nichts.

**Der Generator hat seinen ersten echten Test bestanden** — allerdings erst nach
Reparatur. Der Testlauf davor fand sechs Defekte, die ein reiner `ast.parse`-Test
nicht sehen konnte: eine fehlende `base.py`, `postgresql_where` an einem
`UniqueConstraint`, 20 deklarierte Abhängigkeiten (darunter die gelöschten
`worker-cqrs`/`worker-exceptions`), eine zweite `Base` samt UoW im
`database/__init__.py`, `__init__`-Dateien mit Importen auf entfernte Module,
28 ruff- und 28 mypy-Fehler. Die Tests prüfen jetzt Importierbarkeit und die
Qualitätsgates, nicht mehr nur Syntax.