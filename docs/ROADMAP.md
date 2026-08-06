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
| 3 | Candidate Core | ✅ | 3.1–3.5 komplett (Consent, Profile, Resume, Portfolio, Ablage) |
| 4 | Jobs & Applications | ✅ | 4.1–4.4 komplett (Jobs, Bewerbungen, Unternehmen, Karriere-Seiten) |
| 5 | Transfermarkt | ✅ | 5.1–5.3 (Marktstatus, Vorgang, Tür + Oberfläche). DoD erfüllt; Verträge/Unterschrift gehören zu Phase 8, der Transferberater zu Phase 7 |
| 6 | Developer Intelligence | ✅ | 6.1 (GitHub als Beleg), 6.2 (Passung im Browser), 6.3 (Vokabular) — Score, Ranking und Wechselwahrscheinlichkeit bewusst **nicht** gebaut (ADR-0022/0023) |
| 7 | AI Agent Plattform | ✅ | 7.1 (Naht + Candidate-Agent), 7.2 (Company-Agent: die eigene Anzeige). DoD erfüllt, mit zwei begründeten Abweichungen (plan-act-reflect, „Consents und Logs"). **Candidate Ranking** ist nach ADR-0022 dauerhaft ausgeschlossen; Skill Analyzer, Salary Recommendation und Team Analyzer sind in ihrer ÜBLICHEN Form ausgeschlossen, nicht als Idee |
| 8 | Contracts & E-Signature | ⬜ | Templates, Rechtsprüfung, E-Sign, Audit |
| 9 | Messaging/Notif./Search/Analytics | ✅ | 9.1–9.3 (Outbox in allen drei Diensten, Suche geprüft, Kennzahlen). DoD erfüllt; `worker-messaging` und `worker-search` gelöscht (ADR-0025/0026) |
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

**Folge-Defizite aus 2.5**:
- ~~Alt-Templates mit lokalem `Mediator`/`PipelineBehavior`~~ — **erledigt.** Beim
  Nachprüfen am 05.08.2026 existierten sie nicht mehr: die Vorlagen verweisen
  ausdrücklich auf `worker_platform.application.cqrs`, und `application/__init__.py`
  sagt im Docstring, warum ein service-eigener Mediator ein Duplikat wäre. Der
  Eintrag stand seit dem Umbau der Vorlagen zu Unrecht als offen.
- ~~`worker-github` bleibt unimportierbar~~ — **erledigt am 03.08.2026 durch
  Löschen, nicht durch Reparieren** (ADR-0022). Die damalige Einschätzung „Fix
  gehört in Phase 6" war falsch: repariert hätte der Import 318 Zeilen scharf
  gestellt, die einen Menschen zu einer Zahl zwischen 0 und 100 verrechnen.
- `worker-ai` bleibt aus dem Workspace exkludiert (ML-Wheels ohne
  Python-3.14-Rad). Für Phase 7 über optionale Extras zu lösen, analog weasyprint.
- ~~per-IP-Rate-Limiting am Auth-Rand offen~~ — **erledigt am 05.08.2026.** Kein
  `Authorization`-Header **und** kein Cookie ⇒ 401; und der Anmeldeweg hat jetzt
  eine Bremse. Siehe „Bremse am Auth-Rand" unten.

### Phase 3 — Status: ✅ erledigt (3.1–3.5)

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
  Mediator lohnt erst mit Pipeline-Behaviors. `test_eventbus_seam.py` fehlt —
  und bleibt weg, solange es keinen Publisher gibt: ein Test auf eine Naht ohne
  Gegenstück prüft, dass nichts passiert, und das tut er auch, wenn die Naht
  falsch ist.
  ~~Die Templates erzeugen noch `worker_database.Base`~~ — **erledigt am
  05.08.2026, und der Rest des Fehlers war größer als die Notiz.** Siehe „Acht
  Dienste, ein Datengrab" unten.
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
- ✅ **3.4 Portfolio-Service** (`apps/portfolio-service`) — 02.08.2026. [Design](superpowers/specs/2026-08-02-portfolio-service-design.md).
  Freigabe **wie beim Profil** (eine Capability `portfolio.visibility:public`
  für alle Unternehmen), nicht wie beim Lebenslauf je Unternehmen: ein Portfolio
  ist absichtlich ein Schaufenster, und was nicht gezeigt werden darf, gehört
  nicht hinein. Verworfen: Sichtbarkeit je Eintrag — sie verlagert eine
  schwierige Entscheidung in ein Formularfeld, das beim zwölften Eintrag niemand
  mehr bewusst setzt.
  Getrennte Capability trotz gemeinsamem Schalter in der Oberfläche, damit sie
  einzeln widerrufbar bleibt; ein Test belegt, dass die Profilfreigabe das
  Portfolio NICHT öffnet.
  **Nur `http`/`https` in Links** — ein Portfolio-Link wird von fremden Menschen
  angeklickt, `javascript:` und `data:` sind dort kein Randfall.
  Oberfläche: `/portfolio` mit eigenem Freigabeschalter; eine fünfte
  Playwright-Reise belegt im Browser, dass die Profilfreigabe das Portfolio
  nicht öffnet und erst die zweite Freigabe es tut.
  **Dateien** warten auf einen Konsumenten der Ablage (siehe 3.5); heute ist
  das Portfolio Text und Links.
- ✅ **3.5 Ablage real gemacht** — 02.08.2026, **ADR-0021**. „Re-include" war die
  falsche Handlung: die beiden Pakete waren nicht ausgeschlossen, weil Python
  3.14 neu ist, sondern weil sie sich für jede denkbare Zukunft gleichzeitig
  gerüstet hatten — fünf schwere Abhängigkeiten (pillow, python-magic, boto3,
  minio, azure-storage-blob) über 400 Zeilen ohne einen einzigen Konsumenten.
  `worker-files` ist **gelöscht**; `worker-storage` ist neu geschrieben als Port
  plus `LocalStorage`, mit **einer** Abhängigkeit (`worker-core`) und wieder im
  Workspace, ohne Ausnahme in mypy oder CI.
  Typerkennung aus den ersten Bytes statt aus dem, was der Client behauptet —
  ein `Content-Type`-Header und eine Dateiendung sind beide frei wählbar, die
  Signatur nicht. `write + rename` statt direktem Schreiben, damit ein Absturz
  keine halbe Datei unter dem richtigen Namen hinterlässt.
  **Kein S3-Backend**, noch nicht: es zu bauen, bevor eine Umgebung es braucht,
  wäre genau der Fehler, der zu diesem ADR geführt hat. Der Port ist die Naht.
  **Erster Konsument ist da:** `portfolio-service` nimmt Anhänge entgegen
  (`POST /portfolios/me/attachments`) und liefert sie aus
  (`GET /portfolios/{id}/attachments/{name}`) — mit **derselben** Consent-Prüfung
  wie das Portfolio selbst, damit der Anhang kein zweiter Weg an dieselben Daten
  wird. Der Schlüssel entsteht aus Person UND Name, also greift ein fremder Name
  strukturell nur ins eigene Verzeichnis. Der Name wird vom Server vergeben und
  die Endung folgt dem erkannten Typ: den Namen des Clients zu übernehmen hieße,
  fremden Text zu einem Teil eines Pfades zu machen. Ausgeliefert wird als
  Download, nicht inline — ein PDF kann Skripte enthalten.
  Verwaiste Dateien werden beim Speichern aufgeräumt — **nach** dem Commit:
  andersherum wären bei einem fehlgeschlagenen Commit Dateien gelöscht, auf die
  die gespeicherten Einträge weiterhin zeigen, und aus dem Aufräumen würde
  Datenverlust. Scheitert das Aufräumen, bleibt eine Datei liegen: sie kostet
  Platz und sonst nichts.
  Oberfläche: ein Datei-Feld je Arbeit, das sofort hochlädt. Der lokale
  Dateiname wird bewusst nicht angezeigt — er wandert nicht zum Server, und ihn
  zu zeigen würde suggerieren, dass er es täte.
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
  Oberfläche: `/company/team` (Mannschaft, Einladen, Zurückziehen) und
  `/invitation` (Annehmen über den Link aus der Mail), dazu eine vierte
  Playwright-Reise.
  Entfernen ist dabei: `DELETE /companies/{id}/members/{user}`, nur für
  Administratoren, und **der letzte Administrator kann nicht gehen** — ein
  Unternehmen ohne Administrator wäre nicht gelöscht, sondern verwaist (Domain
  beansprucht, niemand kann mehr einladen, auflösbar nur noch von Hand). Der
  Entzug wirkt beim nächsten Refresh; ein bereits ausgestelltes Access-Token
  bleibt bis zu seinem Ablauf gültig — derselbe bekannte Rest wie beim Abmelden.
  Damit ist Scheibe C abgeschlossen.
  ✅ **Die Mitgliedschaftsprüfung in `handle_refresh` ist vorgezogen und erledigt**
  (02.08.2026). Sie war als Folgearbeit zum Entfernungspfad notiert, wurde aber
  vorher gebaut: so ist der Entfernungspfad vom ersten Tag an sicher, statt eine
  Lücke zu öffnen, die man danach schließen müsste. Ein entzogener Zugang wirkt
  jetzt beim nächsten Refresh; die Sitzung überlebt und die Person handelt wieder
  als Person. Nebenbei repariert: `_FakeTokens` gab in den Unit-Tests immer
  `tenant_id=None` zurück und konnte über den Tenant im Refresh deshalb gar
  nichts aussagen.

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

### Phase 3 — Nachtrag 3.4 und 3.5

- ✅ **3.4 Portfolio-Service** — 02.08.2026.
  [Design](superpowers/specs/2026-08-02-portfolio-service-design.md).
  Arbeiten mit Links und Anhängen. Die Freigabe läuft wie beim Profil
  (`portfolio.visibility:public`), nicht wie beim Lebenslauf: ein Portfolio ist
  ein Aushang, kein Dossier.
- ✅ **3.5 Ablage** — 02.08.2026. **ADR-0021**: `worker-files` gelöscht,
  `worker-storage` auf einen `Storage`-Port plus `LocalStorage` eingedampft.
  Der Inhaltstyp kommt aus den Magic Bytes, nicht aus dem, was der Browser
  behauptet; PNG/JPEG/PDF und sonst nichts. Eine Ablehnung verrät nicht, was
  erkannt wurde.

### Phase 4 — Status: ✅ erledigt (4.1–4.4)

- ✅ **4.1 Jobs-Service** — Stellen sind **öffentlich lesbar**; der
  Consent-Ledger kommt nicht vor, weil niemand betroffen ist, der einwilligen
  könnte. [Design](superpowers/specs/2026-08-02-jobs-service-design.md).
- ✅ **4.2 Applications-Service** — der Punkt, an dem beide Achsen
  aufeinandertreffen. **Verweisen statt kopieren:** eine Bewerbung erteilt
  empfängerbezogene Einwilligungen und widerruft sie beim Zurückziehen. Eine
  Kopie ließe sich nicht widerrufen, und wer einmal kopiert hat, hat für immer.
  [Design](superpowers/specs/2026-08-02-applications-service-design.md).
- ✅ **4.3 Companies-Service** — Arbeitgeberprofile. Getrennt von `tenants` im
  identity-service: das eine weiß, wer ein Unternehmen ist, das andere, wie es
  sich zeigt. [Design](superpowers/specs/2026-08-02-companies-service-design.md).
- ✅ **4.4 Karriere-Seiten** — Pfad statt Subdomain (`/karriere/<kürzel>`), weil
  eine Subdomain eine Betriebsentscheidung ist und kein Anwendungscode. Das
  Kürzel ist abgeleitet und unveränderlich: **die Adresse ist ein Versprechen.**
  Ausdrücklich **keine** personalisierte Seite je Bewerber — sie verriete jedem
  mit dem Link, dass diese Person auf der Plattform ist und dieses Unternehmen
  um sie wirbt. [Design](superpowers/specs/2026-08-02-career-pages-design.md).

### Phase 5 — Status: ✅ erledigt (05.08.2026, 5.1–5.3)

- ✅ **5.1 Marktstatus** — 02.08.2026.
  [Design](superpowers/specs/2026-08-02-transfer-service-design.md).
  Die Zustandsliste des ULTRAPLAN mischte **drei Gegenstände** in einer Reihe:
  die Person, ihr Arbeitsverhältnis, den Arbeitgeber und einen einzelnen
  Vorgang. Als ein Feld modelliert wäre das auf eine Weise falsch, die man
  später nicht mehr auseinanderbekommt. Aufgeteilt: Marktstatus (drei Zustände,
  alle Übergänge erlaubt, Voreinstellung `UNAVAILABLE`), Transfer als eigener
  Vorgang, „Under Contract" als Angabe statt Zustand.
  Sichtbarkeit **nur empfängerbezogen**, es gibt bewusst kein
  `market.visibility:public`: ein Lebenslauf verrät, wo jemand war — der
  Marktstatus verrät, dass er weg will.
- ✅ **5.2 Transfer-Vorgang** — 02.08.2026 (PR #17).
  [Design](superpowers/specs/2026-08-02-transfer-deals-design.md).
  **Der Fund, der den Plan verändert hat:** der ULTRAPLAN verlangt „beschäftigt →
  Firma muss mitwirken". Das lässt sich so nicht bauen — **die Plattform weiß
  nicht, wo jemand arbeitet**, und soll es nicht wissen: ein Datensatz, der
  „arbeitet bei X" mit „hört zu" verbindet, wäre genau die Auskunft, die jemanden
  den Arbeitsplatz kostet, in einer einzigen Tabelle.
  Stattdessen bestätigt die **Person selbst** die Freigabe. Schwächer (niemand
  prüft es) und sicherer.
  **Ablehnen geht immer**, aus jedem laufenden Zustand, von beiden Seiten — ein
  Verfahren, aus dem man nicht aussteigen kann, ist kein Verfahren, sondern eine
  Falle. Genau ein laufender Vorgang je (Person, Unternehmen), als Teilindex.
  Die Ablöse wird festgehalten, nicht bewegt.
- ✅ **5.3 Die Tür und die Oberfläche** — 02.08.2026.
  [Design](superpowers/specs/2026-08-02-market-access-and-ui-design.md).
  **Der Fund:** der Transfermarkt war **unerreichbar**. Ein Vorgang setzt
  `market.visibility:tenant:<id>` voraus — aber kein Endpunkt und keine Seite
  erteilte diese Freigabe je. 5.1 hat es offen gelassen, 5.2 hat es
  vorausgesetzt. Technisch fertig, praktisch tot.
  Jetzt fragt das Unternehmen (`POST /market/{id}/requests`), die Person
  antwortet. **Nicht** an der Lebenslauf-Freigabe mitgehängt, und **nicht** die
  Kontaktaufnahme selbst: sonst bekäme eine Person, die gerade nicht
  angesprochen werden will, genau die Ansprache, gegen die `unavailable`
  existiert. Zwei Stufen, jede einzeln mit Nein beantwortbar.
  Oberfläche: `/markt`, `/transfers`, `/company/transfers`, `/candidates`
  erweitert; zwei Playwright-Reisen über den vollständigen Weg.
  **Zwei Fehler, die nur der Browser fand:** ein `useQuery` hinter einem frühen
  Rückgabesprung (`Rendered more hooks than during the previous render`), und
  eine Oberfläche, die den Abschluss beim Unternehmen vermutete, während die
  Bestätigung der Person ihn selbst auslöst. Beide haben jetzt einen Test, der
  ohne den Fix rot wird.

**Phase 5 ist gegen ihre eigene DoD erledigt.** Sie lautet: *„State-Machine-Tests,
ein voller Happy-Path inkl. Consent-Checks; Soll-Bruch-Pfade (Ablehnung,
Vertragsende) getestet."* Alles davon steht, mit zwei E2E-Reisen.

Sie stand trotzdem lange auf 🟧, und das war ein Buchhaltungsfehler von mir:
unter „offen" hingen drei Punkte, die **anderen Phasen gehören**. Der ULTRAPLAN
nennt sie im Inhaltsteil von Phase 5, aber die DoD entscheidet — und Phase 8
heißt wörtlich „Contracts, E-Signature & Verträge".

Umgehängt statt weiter mitgeschleppt:

- **Vertragsvorlagen** und **digitale Unterschrift** → **Phase 8**. Der Entwurf
  steht ([Design](superpowers/specs/2026-08-02-contract-templates-design.md)),
  gebaut wird nichts, bis sieben rechtliche Fragen beantwortet sind — die nach
  der Formvorschrift zuerst, weil sie den ganzen Schnitt umwerfen kann.
  Ausdrücklich zurückgestellt.
- **`worker-player-advisor` / Transferberater** → **Phase 7**. Ein
  KI-Entwurfserzeuger, und seit 7.1 gibt es die Naht dafür.
  **Nachtrag 05.08.2026, nach Abschluss von Phase 7: nicht gebaut, und der
  Grund ist kein Zeitmangel.** Der ULTRAPLAN führt den Transferberater unter
  den *Candidate*-Agenten — er berät also die **Person**. Im Transfer-Vorgang
  schreibt die Person aber nirgends Freitext: `accept-talk`, `accept-offer`,
  `confirm-release` und `decline` sind vier Knöpfe. Die einzigen zwei
  Freitextfelder (`ExpressInterestV1.message`, `MakeOfferV1.note`) gehören dem
  **Unternehmen**. Der Agent hat also schlicht keine Fläche, an der er ansetzen
  könnte; ihm eine zu geben hieße, der Person einen neuen Schreibschritt in den
  Vorgang zu bauen — eine Produktentscheidung, keine Fortsetzung.
  Und die naheliegende Umkehrung ist es erst recht nicht: Die
  Interessensnachricht ist ein Text **über und an einen bestimmten Menschen**.
  Damit ein Modell sie nützlich formuliert, müsste es etwas über diese Person
  wissen — genau das, was ADR-0024 ausschließt. Ohne dieses Wissen bleibt eine
  Floskel, und eine Floskel ist schlechter als der Satz, den ein Mensch selbst
  tippt. Der Transferberater bleibt deshalb offen, mit Frage statt mit Termin.

Zum Nachlesen, was für Verträge festgelegt ist:

- **Vertragsvorlagen** — [Entwurf steht, gebaut wird nichts](superpowers/specs/2026-08-02-contract-templates-design.md).
  Festgelegt ist der Weg: die Plattform stellt **Vorlagen**, füllt aus, und
  danach geht der Entwurf **in die Prüfung** — erst wenn dort alles stimmt,
  steht er zur Unterschrift bereit. Sie erzeugt keinen Vertrag, und sie füllt
  nur, was sie schon weiß und was beide Seiten schon vereinbart haben (Name,
  Unternehmen, Startmonat, Ablöse, Rolle). Gehalt, Fristen, Probezeit bleiben
  Platzhalter; eine Vorbelegung mit „üblichen Werten" sähe aus wie eine
  Empfehlung und würde als eine gelesen.
  Ab „in Prüfung" ist der Entwurf eingefroren — auch für Tippfehler, besonders
  für Tippfehler.
  **Gebaut wird davon nichts**, bis sieben rechtliche Fragen beantwortet sind;
  die nach der Formvorschrift zuerst, weil sie den ganzen Schnitt umwerfen kann.
- **Digitale Unterschrift** — hängt an derselben Frage.
- **`worker-player-advisor`** — KI-Entwürfe, niemals autonome Verhandlung.

### Querschnitt — Benachrichtigungen ✅ (02.08.2026)

[Design](superpowers/specs/2026-08-02-notifications-design.md).
Die Lücke stand wortgleich unter 3.3, 4.2, 5.2 und 5.3: „Eine Anfrage erreicht
nur, wer sich anmeldet." Jetzt melden sich vier Vorgänge.

**Der Fund:** die naheliegende Mail — *„Acme GmbH möchte deinen Marktstatus
sehen"* — ist die gefährlichste Zeile, die dieses System schreiben könnte. Eine
Mail landet womöglich im Postfach beim aktuellen Arbeitgeber. Also sagt eine
Benachrichtigung **nicht, worum es geht**: kein Firmenname, kein Vorgangstyp,
keine Anzahl. `NotifyV1` hat deshalb gar kein Textfeld.
Und weil auch der **Zeitpunkt** etwas verrät: höchstens eine Mail je Person und
Stunde, über alle Arten hinweg.

Im identity-service statt in einem eigenen Dienst — der bräuchte die
E-Mail-Adresse, und sie zu vervielfachen wäre der Preis für eine Textmail.
Feuern und vergessen: ein Fehlschlag darf niemals den Vorgang kippen, der ihn
ausgelöst hat. Ein Test mit absichtlich kaputtem Notifier zeigte, dass diese
Zusage zunächst nur im Adapter lebte — jetzt steht sie im Router jedes rufenden
Dienstes.

### Querschnitt — „Meine Freigaben" ✅ (02.08.2026)

[Design](superpowers/specs/2026-08-02-my-consents-design.md).
Der Ledger konnte `grant`, `revoke`, `delete` und `check` — aber nicht *„zeig
mir, was gerade gilt."* Damit fehlte einer Plattform, die sich über
Einwilligung definiert, genau die Seite, auf der Einwilligung sichtbar wird:
die Freigaben lagen über vier Seiten verstreut, und die aus einer Bewerbung
(4.2) standen auf keiner davon.

`GET /consent/me` und `/freigaben`. **Nur die eigenen** — es gibt keinen
`subject_id`-Parameter, weder im Pfad noch in der Abfrage: eine fremde Liste
enthielte, welche *anderen* Unternehmen Zugriff haben. Nur Wirksames, keine
Historie, kein Widerrufsgrund.

Der tragende Test: **die Liste und `/check` dürfen sich nie uneinig sein.**
Zwei Wege an dieselbe Auskunft, die auseinanderlaufen können, sind schlimmer
als kein zweiter Weg.

### Querschnitt — Kandidatensuche ✅ (02.08.2026)

[Design](superpowers/specs/2026-08-02-candidate-search-design.md).
`/candidates` zeigte jedes freigegebene Profil nach Änderungsdatum, zwanzig auf
einmal. Wer jemanden mit Python in Berlin suchte, blätterte — der Unterschied
zwischen einer Vorführung und einem Produkt.

Drei Filter: Fähigkeiten (**UND**, Groß-/Kleinschreibung egal), Ort
(Teilstring), Remote (**nur in eine Richtung** — `remote_ok = false` heißt
„nicht ja gesagt", nicht „lehne ab", und ein Filter darauf schlösse Menschen
aus, die schlicht nichts angekreuzt haben).

**Keine neue Einwilligungsfrage:** durchsucht wird ausschließlich, was ohnehin
jedem Unternehmen sichtbar ist. Erst filtern, dann den Ledger fragen — die
Reihenfolge umzudrehen wäre schneller und hieße, die Sichtbarkeit in die
Datenbank des Profildienstes zu legen. Ein Integrationstest legt zwei Profile
mit derselben Fähigkeit an, gibt eines frei, und der Filter findet genau eines.

### Querschnitt — „Was liegt an" ✅ (02.08.2026)

Nach dem Anmelden stand die Werbung. Wer schon da ist, muss nicht überzeugt
werden — er muss wissen, was auf ihn wartet. Die Startseite zeigt Angemeldeten
jetzt genau das.

**Zusammengesetzt im Browser, nicht im Backend.** Ein Dienst, der diese
Übersicht liefert, müsste über vier Dienstgrenzen hinweg lesen — genau das, was
ADR-0004 ausschließt. Die Oberfläche fragt jeden Dienst nach dem, wofür er
zuständig ist; es sind dieselben Abfragen wie auf den Einzelseiten, und der
Cache teilt sie.

**Gezählt wird nur, was eine Handlung erwartet** — nicht, was von selbst läuft.
Sonst wäre es eine Liste, und Listen übersieht man. Und schlägt eine Abfrage
fehl, sagt die Seite das, statt „nichts liegt an" zu behaupten: das ist die eine
Aussage, die nach einem Fehler falsch sein kann und in Sicherheit wiegt.

### Querschnitt — „Meine Daten" ✅ (02.08.2026)

[Design](superpowers/specs/2026-08-02-my-data-design.md).
Eine Person konnte sehr genau steuern, wer was sieht — aber nicht sehen, **was
überhaupt über sie gespeichert ist**, und es nicht mitnehmen. Bei einer
Plattform, deren These „du entscheidest" lautet, die auffälligste verbliebene
Lücke: wer nicht weiß, was da ist, entscheidet über etwas, das er nicht kennt.

Zwölf Abschnitte aus acht Diensten, **zusammengesetzt im Browser** (ADR-0004),
als JSON zum Mitnehmen. Ein neuer Endpunkt: `GET /consent/me/history`.

**Die Geschichte gehört in die Auskunft, nicht in die Übersicht.**
`/consent/me` zeigt bewusst nur, was gilt — eine Historie verrät, wer *einmal*
gefragt hat. In einer Auskunft an die betroffene Person ist genau das richtig,
inklusive Widerrufsgrund: nach außen bleibt er verborgen, ihr gegenüber gibt es
keinen Grund dafür.

**Die tragende Eigenschaft:** ein fehlender Abschnitt wird nicht weggelassen,
sondern als fehlend ausgewiesen — und die Seite sagt es vor dem Herunterladen.
Stillschweigend auszulassen wäre hier der schlimmste Fehler: die Datei sähe
vollständig aus.

### Querschnitt — Kopfzeile aufgeräumt ✅ (03.08.2026)

Nach zehn neuen Seiten in dieser Session standen **siebzehn Einträge**
nebeneinander in der Kopfzeile. Siebzehn gleichwertige Einträge sind keine
Navigation, sondern eine Liste.

Oben bleibt jetzt, was den Transfermarkt ausmacht — Stellen, Marktstatus,
Gespräche. Alles Verwaltende liegt in zwei aufklappbaren Menüs („Mein Konto",
„Unternehmen"), **als natives `<details>`**: Tastatur, Escape und Fokus kann der
Browser bereits, und ein selbstgebautes Menü, das das *nicht* kann, wäre
schlechter als eine Liste. Der Anfragen-Zähler steht an der Zusammenfassung,
nicht an einem Eintrag darin — eine Anfrage, die man erst nach dem Aufklappen
sieht, erreicht niemanden.

Nebenwirkung, die auffiel: neun E2E-Reisen warteten auf Navigationslinks als
Signal für den Unternehmenswechsel. Die liegen jetzt im Menü und sind zugeklappt
unsichtbar. Ersetzt durch die Menü-Zusammenfassung, die es nur mit aktivem
Unternehmen gibt — ein ehrlicheres Signal als ein Link, der auch ohne Wechsel
existieren könnte.

### Phase 6 — Status: ✅ erledigt (05.08.2026, 6.1–6.3)

- ✅ **6.1 GitHub als Beleg, nicht als Note** — 03.08.2026.
  [Design](superpowers/specs/2026-08-03-github-evidence-design.md).
  Beginnt da, wo **ADR-0022** es verlangt: bei der Einwilligung, nicht bei einer
  Formel. Gezeigt werden öffentliche Repositories mit Link — keine Punktzahl,
  kein Rang, keine abgeleitete Eigenschaft. Ein Test prüft, dass `Repository`
  genau sechs Felder hat, damit ein `score` nicht unbemerkt hineinwächst.
  **Die tragende Entscheidung: einmal lesen, nicht zusehen.** Kein
  Hintergrundabgleich, kein Nachtlauf, kein Webhook. ADR-0004 verbietet
  Scraping; der Buchstabe wäre mit einem periodischen Abruf eingehalten, der
  Sinn nicht — eine Plattform, die einem Menschen dauerhaft hinterhersieht, tut
  etwas anderes als eine, die einmal auf seine Bitte hinsieht.
  Der Nachweis läuft über einen öffentlichen Gist (Beschreibung, nicht Inhalt),
  weil es noch keine OAuth-App gibt. Wer den Namen auf ein anderes Konto ändert,
  verliert ihn — sonst stünde fremde Arbeit unter dem eigenen Profil.
  **Zwei Generator-Fehler kamen dabei ans Licht:** ein Servicename ohne
  `-service` erzeugte das Modul `github` (genau der Name, an dem `worker-github`
  zerbrochen ist), und die Vorlage verdrahtete die **Auth-Middleware nicht** —
  jeder Endpunkt antwortete 401, obwohl ein gültiges Token mitkam. Jeder
  bestehende Dienst hatte das von Hand nachgetragen. Beides jetzt in der Vorlage
  bzw. als Riegel im Generator, beides mit Test.

- ✅ **6.2 Passung — in die andere Richtung** — 05.08.2026.
  [Design](superpowers/specs/2026-08-03-matching-design.md).
  Der ULTRAPLAN nennt „Scout-Match", und das übliche Bild dahinter — eine
  Kandidatenliste mit Prozentzahl — ist **der Gesamtscore aus ADR-0022 durch
  die Hintertür**. Deshalb umgekehrt gebaut: **die Passung sieht die Person,
  nicht das Unternehmen, und sie ordnet Stellen, nicht Menschen.**
  Gezeigt wird eine Liste, keine Zahl: „Du hast 2 von 3 genannten Fähigkeiten:
  Python ✓ · Kubernetes ✓ · Go ✗". Kein Prozentwert — eine Prozentzahl sieht
  aus wie eine Messung, ist eine Division und verschweigt genau das, was zählt,
  nämlich **welche** Fähigkeit fehlt.
  **Gerechnet wird im Browser** (`apps/web/src/jobs/match.ts`), aus dem eigenen
  Profil und der Liste der Stelle. Damit existiert die Passung nirgends als
  Datensatz: was es nicht gibt, kann auch nicht ausgewertet werden.
  Neu in der Domäne ist nur `Job.skills` (≤ 20 Einträge, je ≤ 50 Zeichen,
  getrimmt und ohne Rücksicht auf Groß-/Kleinschreibung entdoppelt) plus
  Migration `0002_job_skills`.
  **Beim Bauen fiel eine Zahl auf, die der Entwurf falsch hatte:** er sah 60
  Zeichen je Anforderung vor, das Profil lässt 50 zu. Eine Anforderung, die
  länger ist, als eine Person sie eintragen kann, wäre garantiert nie ein
  Treffer — eine Zeile, die für niemanden je ein Haken werden kann. Das sieht
  man in keiner der beiden Dateien, deshalb hält es
  `tests/test_skill_limits_align.py` fest: kleiner darf die Stelle sein,
  größer nicht.
  Wer nicht angemeldet ist, sieht die Anforderungen ohne Abgleich; wer
  angemeldet ist und **nichts eingetragen** hat, bekommt einen Hinweis auf sein
  Profil statt „0 von 3" — das wäre eine Aussage über ihn, die nicht stimmt: er
  hat nichts gesagt, nicht nichts gekonnt.
  **Und dieselbe Lehre zum dritten Mal:** die Testhilfe `registerAndConfirm`
  wartete nur auf die Erfolgsüberschrift und lief bei einem Fehlschlag 30
  Sekunden in ein nichtssagendes „element(s) not found". Sie wartet jetzt — wie
  `login` — auf **beide** Ausgänge und sagt, welcher eingetreten ist.

- ✅ **6.3 Skill-Graph: das Vokabular, nicht das Urteil** — 05.08.2026.
  [Design](superpowers/specs/2026-08-05-skill-graph-design.md) · **ADR-0023**.
  Der ULTRAPLAN wollte hier Fähigkeiten aus Commits ableiten, zehn Dimensionen
  berechnen, eine **Wechselwahrscheinlichkeit** und einen Match-Score. Nach
  ADR-0022 und 6.2 bleibt davon eines übrig — und es ist das einzige, das
  niemandem etwas unterstellt: **ein Vokabular.**
  Der Bedarf war nicht theoretisch, 6.2 hat ihn erzeugt: die Stelle verlangt
  „PostgreSQL", im Profil steht „Postgres", Ergebnis ✗ — eine Lücke, die es
  nicht gibt, gezeigt an einen Menschen, der sich deshalb womöglich nicht
  bewirbt.
  `packages/worker-skills`: eine Tabelle und eine Funktion, **ohne eine einzige
  Abhängigkeit**. Angewandt **im `Skills`-Wertobjekt** von `profile-service`
  und `jobs-service`, nicht im Router — damit gilt es auch für Zeilen, die vor
  diesem Schnitt geschrieben wurden, und es braucht **kein
  Datenmigrations-Skript**. Reihenfolge tragend: erst umbenennen, dann
  entdoppeln.
  **Die Grenze steht in ADR-0023 und wird von Tests bewacht:** „Postgres" =
  „PostgreSQL" ist eine Aussage über *Sprache*; „React heißt, du kannst auch
  JavaScript" wäre eine über einen *Menschen* — sie schreibt ihm etwas zu, dem
  er nicht widersprechen kann. Kein Niveau, kein Gewicht, keine Verwandtschaft,
  keine Wechselwahrscheinlichkeit.
  Und: es **lehnt nie ab und erfindet nie**. Unbekanntes bleibt wie getippt —
  eine Liste erlaubter Fähigkeiten wäre eine Behauptung darüber, welche Arbeit
  es gibt, und läge bei jedem Beruf außerhalb der IT falsch.
  Der erste Test fand einen Fehler in der Tabelle selbst: „postgresql" stand als
  eigener Alias von „PostgreSQL" — ein Name, der zugleich sein eigener Alias
  ist, und damit der Anfang einer Kette.

- ✅ **Und ein Fund, den erst 6.3 ausgelöst hat: der grüne Bericht über einem
  leeren Lauf.** Nach dem Hinzufügen von `worker-skills` starteten
  `profile-service` und `jobs-service` nicht mehr — `ModuleNotFoundError`, weil
  der Quellcode zwar per Bind-Mount im Container liegt, **das venv aber im
  Image** (`/opt/venv`, absichtlich außerhalb von `/app`). Ein neues
  Workspace-Paket braucht also `docker compose up -d --build`, kein `restart`.
  Beide Container standen dabei auf „running" — uvicorn startete in einer
  Schleife neu.
  Die Folge war schlimmer als der Ausfall: `skipWithoutStack()` übersprang
  **alle 16 E2E-Reisen**, und `make validate-e2e` meldete trotzdem *„Alles
  grün"*. Genau das Skript, das es „nicht beim ersten Fehler abbricht und die
  übersprungenen Tests benennt" verspricht, zählte nur Python-Skips.
  `scripts/validate.sh` ist jetzt **rot**, wenn keine einzige Reise gelaufen ist
  — ein einzelner übersprungener Test ist eine Entscheidung, alle sind ein
  Ausfall.

### Phase 9 — Status: ✅ erledigt (06.08.2026)

Die DoD lautet: *„Cross-Service-Event fließt via Outbox, Notification geht raus,
Suche findet, Analytics aggregiert datenschutzkonform. Tests
(Testcontainers)."* — alle vier erfüllt. **Nicht** gebaut wurden die vier
Dienste aus dem Inhaltsteil (`messaging-`, `notifications-`, `search-`,
`analytics-service`): die DoD entscheidet, und was sie verlangt, gibt es
bereits oder ist an der richtigen Stelle entstanden. Zwei Hüllen mussten dafür
weichen — `worker-messaging` (fünf Abhängigkeiten, null Konsumenten) und
`worker-search` (drei Suchmaschinen, null Konsumenten).

- ✅ **9.1 Die Outbox: eine Benachrichtigung, die nicht verlorengeht** —
  05.08.2026. **ADR-0025.** `packages/worker-outbox` + `worker-platform`
  (`background=`) + `apps/transfer-service` als erster Konsument.
  **Der Fehler, den es zu beheben gab.** Benachrichtigungen liefen als „feuern
  und vergessen": nach dem Commit ein HTTP-Aufruf, dessen Fehler geschluckt
  wurde. Die Begründung war richtig — eine misslungene Mail darf keinen Vorgang
  kippen — aber der Preis war, dass die Mail dann **für immer weg** ist. Ein
  Neustart von identity-service genügte, und niemand erfuhr, dass jemand nach
  ihm gefragt hat. Für ein Produkt, dessen Mechanik auf „die Person
  entscheidet" beruht, ist das kein Schönheitsfehler: wer nicht erfährt, dass
  gefragt wurde, kann nicht antworten.
  Die Outbox dreht die Zusage nicht um, sie löst den Widerspruch: die **Absicht**
  wird in DERSELBEN Transaktion wie die fachliche Änderung geschrieben. Kommt
  die Änderung durch, liegt die Absicht fest; wird sie zurückgerollt, ist sie
  weg. Ein Zusteller im Hintergrund darf danach beliebig oft scheitern.
  **Kein Broker — und der dritte Wiedergänger.** Der ULTRAPLAN sieht
  `worker-messaging` vor (aio-pika/aiokafka/nats). Beim Nachsehen: 129 Zeilen,
  **fünf** schwere Abhängigkeiten, drei Umsetzungen, **null Konsumenten**; die
  einzigen Verweise waren zwei `[tool.uv.sources]`-Einträge, und die
  installieren nichts. Wort für Wort `worker-files` (ADR-0021),
  `worker-github` (ADR-0022) und `worker-ai` (ADR-0024). Gelöscht. Ein
  Postgres, das jeder Dienst ohnehin betreibt, trägt eine Outbox mit einer
  Tabelle; `worker-outbox` hat **eine** Abhängigkeit statt fünf.
  Beim Löschen kam heraus, dass `aio_pika` transitiv noch zwei Stellen
  versorgte: einen `RabbitMQHealthCheck` für einen Broker, den es nicht gibt,
  und eine AioPika-Instrumentierung im Tracing. Beide entfernt — ein
  Gesundheitscheck für etwas, das nicht existiert, kann nur eine Antwort geben,
  und die sagt nichts.
  Festgelegt und je mit Test belegt: **dieselbe Transaktion** (gegenbewiesen —
  mit einem eingebauten `session.commit()` wird der Test rot); **Wiederholung
  statt Verlust**; **Aufgeben heißt liegenlassen, nicht löschen**; **kein
  Inhalt in der Tabelle** (nur `user_id` und `kind` — eine Outbox steht danach
  in jedem Backup, ein `payload`-Feld wäre die Einladung, den Nachrichtentext
  mitzuschreiben); nur die **Art** eines Fehlers, nie die Antwort des
  Gegenübers; **älteste zuerst**, sonst kommt „Angebot zurückgezogen" vor
  „Angebot gemacht"; und die Zusteller-Schleife **überlebt einen kaputten
  Durchlauf**, denn stirbt sie, bleibt die Tabelle liegen und niemand merkt es.
  Der Dauerläufer hängt an einem neuen `background=` von `create_api_app`. Zwei
  Fallen dort, beide mit Test: das Herunterfahren muss auf die Aufgabe
  **warten** (sonst bricht es mitten in einer Transaktion ab — der erste
  Testentwurf war auch ohne das `await` grün und bewachte damit nichts), und
  ein Absturz darf **nicht still** sein.
  Zwei bestehende Integrationstests mussten sich ändern und wurden dabei
  strenger: sie prüfen jetzt **zwei** Schritte statt einem — Absicht
  festgehalten, dann zugestellt. Der Zwischenzustand war vorher nicht prüfbar,
  weil es ihn nicht gab.
- ✅ **9.2 Die beiden übrigen Dienste ziehen nach** — 06.08.2026.
  `applications-service` und `resume-service` benutzen denselben Weg wie
  transfer-service: Tabelle an der eigenen `Base`, Migration, `record()` VOR
  dem Commit, Zusteller am `background=`. Damit gibt es im System **keinen**
  Pfad mehr, auf dem eine Benachrichtigung stillschweigend verlorengeht.
  **Der eigentliche Befund war ein anderer:** beide Dienste hatten
  **überhaupt keinen Test auf den Notifier**. Die Zusage „die Person wird
  benachrichtigt" war dort nie geprüft — die Umstellung wäre also eine reine
  Behauptung geblieben. Jetzt hat jeder einen Integrationstest mit kaputtem
  Zusteller (Zeile bleibt liegen → Dienst wieder da → Zustellung), und für
  `applications-service` ist er **gegenbewiesen**: ohne `record()` wird er rot.
  Die zehn Zeilen `outbox_runner` sind je Dienst kopiert, nicht geteilt — ein
  gemeinsames Paket wäre ein Kopplungspunkt über eine Dienstgrenze hinweg
  (ADR-0003/0004), dieselbe Abwägung wie beim Consent- und Notify-Adapter.
  Der `.env.example`-Wächter hat auch hier zugeschlagen und das fehlende
  `WORKER_OUTBOX_INTERVAL_SECONDS` in **beiden** Dateien gefunden — zum dritten
  Mal in dieser Woche ein eigener Fehler, den ein Wächter fing statt ich.

- ✅ **9.3 Suche geprüft, Kennzahlen gebaut** — 06.08.2026. **ADR-0026.**
  **Die Suche gab es schon.** `jobs-service` sucht über Titel, Beschreibung und
  Ort, `profile-service` über Fähigkeiten und Ort — mit Tests und einer
  E2E-Reise („die Suche findet nur, was freigegeben ist"). Daneben stand
  `worker-search`: 223 Zeilen, **drei** Suchmaschinen (elasticsearch,
  meilisearch, qdrant-client), **null Konsumenten** — zum vierten Mal dasselbe
  Muster. Gelöscht; sechs Abhängigkeiten weniger.
  **Die Kennzahlen sind die eigentliche Entscheidung.** Der naheliegende Reflex
  wäre k-Anonymität gewesen — Zahlen erst ab einer Mindestgröße. Für diesen
  Fall ist das aber das falsche Werkzeug: ein Unternehmen sieht seine
  Bewerbungen ohnehin **einzeln**. Eine Schwelle auf der Summe verdeckte etwas,
  das daneben im Klartext steht — Theater, das vom eigentlichen Punkt ablenkt.
  Die Grenze liegt **nicht bei der Aggregation, sondern bei der
  Zusammenführung**: zulässig ist eine Zahl, die keine Auskunft erzeugt, die
  der Fragende nicht ohnehin hat. „12 Bewerbungen, davon 3 abgelehnt" auf die
  eigenen Stellen ist Bequemlichkeit. „Ihre Bewerber haben sich bei 7 anderen
  Firmen beworben" oder „60 % suchen aktiv" wäre eine Aussage über Menschen aus
  Quellen, die einzeln freigegeben wurden — und keine Aggregation macht das
  wieder gut.
  Gebaut als `GET /companies/me/application-stats` **dort, wo die Daten liegen**,
  nicht als eigener `analytics-service`: der müsste Vorgänge kopieren oder
  dienstübergreifend lesen, gegen ADR-0004, und schüfe einen zweiten Ort, an
  dem personenbezogene Daten liegen und gelöscht werden müssten — für eine
  Zahl, die ein `GROUP BY` beantwortet. Gezählt wird in der Datenbank; die
  Zeilen werden gar nicht geladen.
  Drei Tests: die Abgrenzung (ein fremdes Unternehmen sieht seine eigene leere
  Zahl), die **Feldmenge der Antwort** (`by_status`, `total` — sonst nichts,
  dieselbe Strenge wie bei `DraftContext`) und `403` ohne aktives Unternehmen.
  Bewusst nicht gebaut: Dashboards, Zeitreihen, Trichter-Auswertungen und
  **jede Form von Verhaltens-Tracking**.

### Phase 7 — Status: ✅ erledigt (05.08.2026)

Die DoD lautet: *„≥ 1 Candidate- + ≥ 1 Company-Agent läuft als
Entwurfs-Erzeuger mit plan-act-reflect-Schleife, jedes externe Ergebnis fordert
Human-Review an. Consents und Logs vorhanden. Tests."* — erfüllt durch 7.1
(Candidate) und 7.2 (Company), mit **zwei ausdrücklich begründeten
Abweichungen** bei plan-act-reflect und bei „Consents und Logs", die unter 7.2
ausgeschrieben stehen. Die 21 übrigen Agenten aus `kon.txt` sind **nicht**
Teil dieser DoD; wo ADR-0022 sie berührt, steht das unter 7.1.

- ✅ **7.1 Die Naht zur KI: ein Entwurf, den die Person anfordert** — 05.08.2026.
  [Design](superpowers/specs/2026-08-05-ai-seam-design.md) · **ADR-0024**.
  `worker-ai` war 246 Zeilen mit **acht** schweren Abhängigkeiten, einem
  Smoke-Test und **keinem Konsumenten** — aus Workspace und mypy
  ausgeschlossen, der **letzte verbliebene Skip** der Testreihe. Wort für Wort
  die Lage von `worker-files` vor ADR-0021, und dieselbe Antwort: neu
  geschrieben mit **einer** Abhängigkeit (`httpx`), **einem** Anbieter und
  einem echten Konsumenten. **Die Testreihe hat jetzt null Skips.**
  **Der erste Agent hilft der Person zu sagen, was sie sagen will** — er sagt
  nichts über sie. Das ist keine Feinheit: eine KI, die *über* jemanden
  schreibt (aus Commits, aus dem Lebenslauf), verletzt die These der Plattform;
  eine, die beim Formulieren hilft, bedient sie.
  Vier Regeln, alle mit Test: **nur auf Anforderung** (kein Hintergrundlauf);
  **es wird nichts gespeichert** (weder Prompt noch Antwort — der Entwurf lebt
  im Formular, bis die Person speichert, und dann ist es ihr Text);
  **`DraftContext` ist die Grenze** (Überschrift, Freitext, Fähigkeiten, Wunsch
  — kein Name, keine Adresse, keine `subject_id`; ein Test nagelt die Feldmenge
  fest); **nichts davon im Protokoll**.
  `NullDrafter` ist die Voreinstellung: ohne Schlüssel wird kein fremder Dienst
  angerufen, und die Oberfläche sagt es. Der Hinweis, was hinausgeht, steht **am
  Knopf** und nennt den Anbieter — nicht in einer Datenschutzerklärung.
  **Was ADR-0022 für die übrigen Agenten heißt — präzise, nachdem eine erste
  Fassung dieses Eintrags es zu pauschal sagte.** Dort stand, vier Agenten
  seien „nicht baubar". Die ADR verbietet aber eine **Form**, keinen **Namen**:
  eine Zahl, die einen Menschen zusammenfasst, jede Rangfolge daraus,
  abgeleitete Eigenschaften ohne Grundlage, stillschweigende Vollständigkeit.
  Namen zu verbieten wäre sogar schädlich — es lädt zum Umbenennen ein.
  - **Candidate Ranking** ist der einzige, dessen Name selbst die verbotene
    Sache ist. Dauerhaft ausgeschlossen.
  - **Skill Analyzer** steht im ULTRAPLAN in der **Candidate**-Liste, ist also
    ein Agent für die Person selbst. Verboten war die gelöschte *Umsetzung*
    (`bytes_count / total_bytes` — eine Zahl je Fähigkeit je Mensch, und Bytes
    sind kein Können). Erlaubt wäre „du hast in diesen drei Repositories Python
    geschrieben, hier sind die Links" — ein Beleg mit Herkunft, den die ADR
    ausdrücklich zulässt.
  - **Salary Recommendation** hängt daran, worüber die Zahl etwas sagt: ein
    Gehaltsband für eine *Rolle* fasst keinen Menschen zusammen, ein Betrag für
    *diese eine Person* schon — und wäre eine Empfehlung an den Arbeitgeber,
    wie wenig er ihr bieten kann.
  - **Team Analyzer** ebenso: eine Aussage über eine Zusammensetzung ist etwas
    anderes als eine Eigenschaft je Kopf; die übliche Umsetzung macht das
    Zweite.
  Drei von vier sind also nicht verboten, sondern **in ihrer üblichen Form**
  verboten. Auf der Warteliste dieses Schnitts steht trotzdem keiner — aus
  einem anderen Grund: jeder braucht eine eigene Abwägung, und die gehört in
  den Schnitt, der ihn baut.
  Nebenbei repariert: ein bestehender Test prüfte die Erfolgsmeldung mit
  `/gespeichert/i` und wurde vom neuen Hinweis („Gespeichert wird nichts")
  mitgetroffen — dieselbe Lehre wie beim E2E-Wackler mit `/bestätigt/i`.

- ✅ **7.2 Der Unternehmens-Agent: die eigene Anzeige verständlicher** —
  05.08.2026. `POST /jobs/draft`, `JobDraftContext`, Knopf im
  Ausschreibungsformular.
  Die DoD verlangt „≥ 1 Candidate- **+ ≥ 1 Company**-Agent"; 7.1 lieferte nur
  den ersten. Von den fünf Unternehmens-Agenten des ULTRAPLAN ist dies der
  einzige, der **ohne eigene Abwägung** baubar war, und der Grund steht schon
  im Namen: Scout, Candidate Ranking, Salary Recommendation und Team Analyzer
  richten sich alle auf **Menschen**, dieser auf einen **Text, den das
  Unternehmen selbst verfasst hat**. Damit ist er die exakte Spiegelung von
  7.1 — dort hilft die KI einer Person zu sagen, was *sie* sagen will, hier
  einem Unternehmen. Über niemanden sagt sie etwas.
  Getrennte Klassen statt einer mit Verzweigungen: `DraftContext` (Person) und
  `JobDraftContext` (Anzeige) bringen über das `Draftable`-Protokoll **ihre
  eigenen Regeln** mit. Ein gemeinsamer Prompt mit `if` wäre die Stelle, an der
  irgendwann „erfinde nichts über die Person" für eine Anzeige gilt — oder
  schlimmer, andersherum. `_SYSTEM_JOB` verbietet zusätzlich, was eine Anzeige
  falsch macht: **keine Anforderungen dazuerfinden** (sonst steht in der
  Ausschreibung etwas, das niemand verlangen wollte, und Suchende gleichen sich
  gegen Erfundenes ab), geschlechtsneutral, keine Superlative, keine
  Altersangabe/Herkunft/Familiensituation.
  `JobDraftContext` trägt **keine `tenant_id` und keinen Firmennamen** — der
  Entwurf braucht beides nicht, und was nicht in der Klasse steht, kann nicht
  hinausgehen. Zwei Tests nageln das an beiden Enden fest: einer die
  `__dataclass_fields__` von `JobDraftContext` (analog zum Test auf
  `DraftContext`), einer die Schlüsselmenge der Nutzlast im Browser. Beide
  müssten geändert werden, damit je eine `tenant_id` mitreist.
  `_company(request)` steht auch an diesem Endpunkt: ohne die Prüfung wäre er
  ein Textgenerator für jeden Angemeldeten. Der Zusammenhang kommt aus dem
  Request statt aus der Datenbank — beim Schreiben gibt es die Anzeige dort noch
  nicht.

  **Zwei Punkte der DoD, bewusst anders gelöst — und deshalb hier
  aufgeschrieben statt stillschweigend übersprungen:**

  - **„mit plan-act-reflect-Schleife" — nicht gebaut, mit Absicht.** Die
    Schleife löst ein Problem, das diese beiden Agenten nicht haben: sie ist
    für Agenten da, die *mehrere Schritte* tun und deren Zwischenergebnis
    geprüft werden muss. Hier gibt es einen Aufruf und einen Text. Eine
    Reflect-Stufe hieße konkret: das Modell bewertet seinen eigenen Entwurf und
    schreibt ihn ohne Rückfrage um — also **zwei Aufrufe, doppelte Wartezeit,
    doppelte Kosten**, und ein Ergebnis, das weiter von dem entfernt ist, was
    die Person eingegeben hat. Die Prüfung, die es hier wirklich braucht, macht
    ohnehin ein Mensch: der Entwurf landet im Formularfeld und wird erst durch
    Speichern zu seinem Text. Das ist das Human-Review aus derselben DoD, und
    es ist das strengere. Sollte je ein mehrschrittiger Agent dazukommen
    (Transferberater), wird die Schleife dort gebraucht und dort gebaut.
  - **„Consents und Logs vorhanden" — der Knopf *ist* die Einwilligung, und
    das Protokoll wäre der Fehler.** Ein Eintrag im Consent-Ledger bedeutet
    dort eine **stehende** Erlaubnis, die man später widerrufen kann. Genau die
    gibt es hier nicht: es gibt einen Klick, einen Aufruf, einen Text, und
    danach nichts mehr, das zu widerrufen wäre. Ein Ledger-Eintrag würde eine
    dauerhafte Erlaubnis behaupten, die niemand gegeben hat — und in
    `GET /consent/me` als solche erscheinen. Stattdessen steht der Hinweis
    **am Knopf** und nennt Anbieter und Umfang, bevor gedrückt wird; informiert
    und je Benutzung. Ein Inhaltsprotokoll ist aus demselben Grund verboten wie
    CVs im Log (`product-scope.md`): der Freitext gehört derselben Klasse an.
    Gemeldet wird die **Fehlerart**, nie Prompt oder Antwort.

  Belegt mit laufendem Docker, also **null Skips** in beiden Reihen und echten
  Testcontainers-Läufen — ein grüner Bericht über eine übersprungene Reihe ist
  schlimmer als ein roter. Eine E2E-Reise prüft dabei genau den Zustand, der im
  lokalen Stapel wirklich herrscht: **kein Schlüssel gesetzt.** Der Knopf
  antwortet mit einem echten 503 aus dem `NullDrafter`, die Oberfläche sagt es,
  und der Text des Unternehmens steht unverändert da. Das ist der gefährlichste
  Fall, weil er still sein könnte: ein Knopf, der nichts tut, sieht aus wie
  einer, der lädt.
  **`scripts/validate.sh` nennt jetzt immer die Stückzahlen** (`pytest N
  bestanden`, `playwright N Reisen`) statt nur Haken. Die Warnungen darin
  schlugen bisher erst an, wenn etwas übersprungen wurde — ein Lauf, der
  stillschweigend nur die Hälfte einsammelt, wäre durchgekommen.
  Nebenbei behoben: der Kommentar an beiden Entwurfs-Knöpfen behauptete,
  `type="button"` verhindere ein Absenden des umgebenden Formulars — `Button`
  gibt das Attribut aber ohnehin vor, der Kommentar beschrieb also einen
  Mechanismus, den es nicht gab. Ein Gegenbeweis zeigte es: der Test blieb grün,
  als das Attribut entfernt wurde. Jetzt sagt der Kommentar die Wahrheit, und
  ein Test hält das gerenderte Attribut fest — der scheitert, wenn beides
  wegfällt (nachgewiesen, nicht angenommen).

### Aufräumen: alles Bekannte behoben (05.08.2026)

Ausgelöst durch eine berechtigte Rückfrage — *„wieso findest du solche Fehler
und behebst sie nicht sofort?"*. Die Antwort war in einem Fall gut (ein Fix
ohne fehlschlagenden Test ist eine Vermutung) und in den anderen nur bequem.
Also: alles, was in dieser Datei als offen stand, geprüft und geschlossen.

- ✅ **Die Bestätigungsseite schickte ihren Token zweimal.** Beleg: 2 von 120
  POSTs auf `/auth/verify-email` kamen im E2E-Lauf als HTTP 400 „ungültig"
  zurück. Der Token ist einmalig — der zweite Aufruf verbraucht ihn nicht, er
  scheitert, und wessen Antwort zuletzt ankommt, entscheidet, was die Person
  sieht. Im schlechten Fall „Bestätigung fehlgeschlagen", während ihr Konto
  gerade freigeschaltet wurde.
  **Warum er beim ersten Anlauf nicht behoben wurde:** ein Test mit
  `<StrictMode>` war auch am ungefixten Code grün, und ein Test, der nicht
  fehlschlagen kann, bewacht nichts. **Was ihn reproduzierbar machte:** nicht
  StrictMode, sondern schlicht ein **zweiter Aufbau** — `render` → `unmount` →
  `render`. Damit war der Test rot, und der Riegel sind drei Zeilen: eine
  modulweite `Map` von Token auf **Zusage**, sodass ein zweiter Aufbau
  dasselbe Ergebnis bekommt statt eines zweiten Aufrufs. Ein `useRef` hätte
  nicht gereicht — er stirbt mit der Komponente, und genau darum geht es.
  **Nachtrag 06.08.2026: dieselbe Seite gab es zweimal, und ich hatte nur eine
  repariert.** `/invitation` trug den Fehler unverändert weiter — auch der
  Einladungstoken ist einmalig. Aufgefallen ist er nicht durch Nachdenken,
  sondern als **Wackler** im E2E-Lauf: einmal rot, beim zweiten Versuch grün,
  also genau die Sorte Befund, die man wegzuklicken versucht ist. Die Folge
  wäre schlimmer als bei `/verify` gewesen: „Einladung nicht angenommen" auf
  dem Schirm, während die Person dem Unternehmen gerade beigetreten war.
  Derselbe Riegel (`acceptOnce`), Test zuerst und **rot gesehen**, danach
  gegengeprüft. Der Riegel legte prompt einen zweiten, kleineren Fehler frei:
  die modulweite `Map` überlebt zwischen Tests, und `invitation.test.tsx`
  benutzte viermal denselben Token `"abc123"` — zwei bestehende Tests wurden
  rot, obwohl die Seite stimmte. `verify.test.tsx` macht es richtig vor und
  zieht je Test einen frischen Token; nachgezogen.
  Belegt: der E2E-Lauf ging von **17 grün + 1 Wackler** auf **18 grün, kein
  Wackler** (und von 8,9 auf 4,6 Minuten).

- ✅ **Acht Dienste, ein Datengrab.** Beim Nachprüfen der Notiz „die Templates
  erzeugen noch `worker_database.Base`" stellte sich heraus: die Vorlage war
  nur die halbe Wahrheit. `base.py` war längst richtig, aber
  **`migrations/env.py` holte die Base weiter aus `worker_database`** — in der
  Vorlage *und* in acht von zehn Diensten. `target_metadata` zeigte damit auf
  eine **leere** MetaData, und `alembic revision --autogenerate` hätte daraus
  eine Migration gebaut, die **jede Tabelle löscht**.
  Gemerkt hat es nie jemand, weil `upgrade head` nur die vorhandenen Skripte
  ausführt: der Stack lief grün über einem Generator, der beim ersten
  Autogenerate ein Datengrab ausgehoben hätte.
  Behoben in allen acht plus der Vorlage. Gesichert durch
  `tests/test_migration_metadata.py` — und zwar auf **Identität** der Base, nicht
  auf „Metadata nicht leer": der erste Entwurf dieses Tests war
  reihenfolgeabhängig, weil `identity-service` seine Modelle tatsächlich auf
  `worker_database.Base` registriert und die geteilte MetaData damit voll
  aussah, sobald seine Tests zuerst liefen. Drei statt acht Fehlschläge — ein
  Test, der den Fehler je nach Laufreihenfolge verschwinden lässt.

- ✅ **Bremse am Auth-Rand** — der einzige `TODO`-Marker, der im Code stand.
  `worker_platform.presentation.throttle`: gleitendes Fenster je Herkunft, als
  Middleware **weiter außen als die Authentifizierung**. Das ist der Punkt und
  nicht Kosmetik: läge sie innen, würde für jeden Rateversuch erst bcrypt
  gerechnet, und die Bremse wäre der teuerste Teil des Angriffs. Der Test
  beweist es ohne Datenbank — zehnmal 500 (Handler erreicht), dann 429 (nicht
  erreicht).
  **Je Herkunft, nie je Adresse:** eine Bremse je E-Mail-Adresse wäre zweimal
  falsch — sie verriete durch ihr Verhalten, dass es die Adresse gibt, und ein
  Fremder könnte damit eine bestimmte Person aussperren.
  Der abgelehnte Versuch wird **nicht** mitgezählt, sonst hielte sich eine
  Sperre selbst am Leben. Genau diesen Fehler hat `worker-ratelimit` (unbenutzt,
  braucht Redis, das nicht im Stack steht) — deshalb eine eigene Umsetzung mit
  einer Abhängigkeit weniger statt drei, die niemand einschaltet (ADR-0021).
  **Aus in LOCAL/TEST**, weil im Compose-Stack jede Anfrage von derselben
  Gateway-Adresse kommt: dort träfe sie die eigene Testreihe statt eines
  Angreifers. Ausdrücklich einschaltbar (`WORKER_AUTH_THROTTLE_ENABLED=true`).

### Weiterhin offen, quer durch alle Phasen

- **`worker-github` ist gelöscht** (03.08.2026, **ADR-0022**). Nicht repariert:
  es war unimportierbar, hatte keinen Konsumenten — und verrechnete einen
  Menschen zu einer Zahl zwischen 0 und 100, aus zehn Dimensionen mit
  Gewichten, die niemand begründet hat. „Können in einer Sprache" maß es als
  Anteil an geschriebenen **Bytes**. Ein Import-Fix hätte 318 Zeilen scharf
  gestellt, die genau die Art von Aussage produzieren, gegen die diese
  Plattform gebaut ist. Die ADR sagt, was in Phase 6 wiederkommen darf (Belege
  mit Herkunft, Einwilligung zuerst) und was nicht (ein Gesamtscore).
  Damit ein Skip weniger; übrig bleibt `worker-ai`.

- **Die E2E-Wackelkandidaten waren zwei Fehler, kein „Last".** Erledigt
  (03.08.2026), und die Diagnose ist die Geschichte wert:
  1. Klicks auf Listeneinträge hatten nur das `actionTimeout` (15 s) statt des
     expect-Budgets. 15 Stellen warten jetzt erst auf den Eintrag.
  2. **Der eigentliche Fund:** die Bestätigungsseite hat drei Überschriften —
     „Wird bestätigt…", „E-Mail bestätigt", „Bestätigung fehlgeschlagen". Die
     Testhilfe prüfte auf `/bestätigt/i` und war damit schon bei der
     **Ladeanzeige** zufrieden. Sie lief weiter, während die Bestätigung noch
     lief oder gerade scheiterte; beim Anmelden kam dann „email not confirmed",
     an einer Stelle ohne Bezug zur Ursache.
  Aufgefallen ist das erst, nachdem die Anmelde-Hilfe auf **beide** Ausgänge
  wartet und bei einem Fehlschlag die Meldung der Seite in die Ausnahme
  schreibt, statt stumm in die Zeitüberschreitung zu laufen. Vorher war jeder
  Fehlschlag gleich aussagelos, und der Verdacht fiel auf die Maschine.
  Ergebnis: 14 von 14 grün, **ohne** Wiederholung — und die Suite braucht 3,5
  statt 9,5 Minuten, weil ein großer Teil der alten Laufzeit Wartezeit auf
  Zeitüberschreitungen war.
  Nebenbei: neun byte-identische Kopien von `login`/`registerAndConfirm` sind zu
  einer in `e2e/stack.ts` geworden.
- **Kein S3-Backend**, solange keine Umgebung eines braucht (ADR-0021).
- **Keine personalisierten Karriere-Seiten** (siehe 4.4).
