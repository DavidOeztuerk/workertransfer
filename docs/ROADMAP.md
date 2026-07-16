# Roadmap

Pull-Through-Index zu [ULTRAPLAN.md](./ULTRAPLAN.md). Status wird hier
gepflegt; Sub-Punkte werden bei Eintritt in eine Phase als Tasks angelegt.

Legende: ⬜ nicht begonnen · 🟧 in Arbeit · ✅ erledigt · ⛔ blockiert

## Phasen

| # | Phase | Status | DoD-Kurz |
|---|-------|--------|----------|
| 0 | Repo-Disziplin & Betriebsamskeit | ✅ | Roadmap, ADRs, Skills, Glossar |
| 1 | Foundation festigen (CI grün) | ✅ | ruff 0 ✅, mypy 0 ✅, CLI läuft ✅, Duplikate ✅, Smoke-Tests ✅, Premerge-Wrapper ✅ |
| 2 | Identity & Tenancy | ⬜ | OIDC/OAuth, JWT, Claims-Tenant, Audit, DB-Migration |
| 3 | Candidate Core | ⬜ | Profile/Resume/Portfolio + Consent-Ledger |
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
  Typer `app`; Entry `worker_cli.main:app` löst jetzt. `new-service` erzeugt
  vollständiges Clean-Arch-Skelett, `new-package` gültiges Paket. Sub-Befehle
  command/query/entity/event/consumer/publisher nutzen leere `cqrs`/`domain`/
  `infrastructure`-Templates = Folge-Defizit.)
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

Nächste Aktion: **Phase 2 starten — Identity & Tenancy.** Erster vertikaler
Slice mit echter Domain: `apps/identity-service` ausbauen (User-Aggregat,
Account-Lifecycle, Sessions, OIDC/OAuth2-Einstieg, JWT-Ausgabe/-Verify,
Refresh-Tokens), `worker-auth` + `worker-authorization` einbinden,
PostgreSQL-Substrat via `worker-database`, Tenant-Kontext von Header-Resolver
auf claims-basiert umstellen (Header-Resolver nur local/test), Audit-Events.
DoD: User kann sich anmelden, erhält JWT, Tenant kommt aus dem Claim, Audit-Event
persistiert, Identity-Service hat DB-Migration, Tests (Testcontainers).