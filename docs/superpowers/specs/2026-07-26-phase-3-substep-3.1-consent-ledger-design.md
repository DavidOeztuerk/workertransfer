# Phase 3 — Sub-step 3.1: Consent-Ledger (Design)

- **Status:** Design (brainstorming-approved, pre-implementation)
- **Date:** 2026-07-26
- **Slice:** Phase 3 → Sub-step 3.1 (Consent-Ledger, der tragende Enabler)
- **Relates:** ULTRAPLAN Phase 3; ADR-0004 §3 (consent enabler); ADR-0007 (HS256/PyJWT);
  ADR-0012 (audit sync-UoW, PII allowlist); ADR-0011 (Testcontainers offline-skip);
  `docs/product-scope.md` (consent + revocation-immediate); `apps/identity-service`
  (fertige Phase-2-Referenzform)

## 1. Ziel & Scope

Der Consent-Ledger ist der **Enabler** für jede künftige Sichtbarkeit/Sendung/Datenimport
in Phase 3–6: „ohne Consent keine Sichtbarkeit“ (product-scope.md; ADR-0004 §3).
Sub-step 3.1 liefert ein **standalone service** `apps/consent-service`, das ein append-only
Verzeichnis über erteilte, entzogene und gelöschte Einwilligungen hält, synchron via HTTP
von konsumierenden Services konsultiert wird, und das produkt-scope-Diktat *„revocation
must immediately withdraw the affected capability"* erfüllt.

**In-Scope (Sub-step 3.1):**
- `apps/consent-service` (Clean-Arch-Spiegel von identity-service).
- append-only `consent_events` + `audit_events` (eigenes Schema, eigene DB).
- HTTP: `POST /consent/grant|revoke|delete|check`.
- Versionierte `worker-contracts`-DTOs (`Consent{Grant,Revoke,Check,State}V1`).
- JWT-Vertrauen aus identity-service (shared HS256-Secret via Settings, ADR-0007).
- Unit + Integration (Testcontainers) + App-Tests; ADR für die Ledger-Topologie.

**Out-of-Scope (später, sequenziell nach Sub-step 3.1):**
- Profile-Service (3.2), Resume-Service (3.3), Portfolio-Service (3.4).
- `worker-files`/`worker-storage` real machen (getrieben ab 3.2/3.4).
- Outbox/Inbox + per-service Projection der Consent-Events (Phase 9).
- Read-Cache/TTL in konsumierenden Services (global: bricht die Immediate-Revocation-Regel).
- Admin-Rollen oder Delegated-Consent-Modelle (Phase 3 vorerst Selbstverwaltung).
- Retention/GDPR-Dateninfonanz (Löschungs-Followup; DELETE-Event bedientSelbstnur Capability Withdrawal, kein Recht auf Vergessen).

## 2. Architektur

`apps/consent-service` ist ein eigenständiger Service, der die fertige `identity-service`-Form
exakt spiegelt (Composition-Root-Disziplin, Clean-Arch-Schichtung, Alembic, Testcontainers,
eigene `PlatformSettings`-Subklasse, 4-Gate). Er besitzt seine eigene PostgreSQL-DB —
**keine geteilte DB, kein Cross-Service-Repo** (sharing rule CLAUDE.md:82; ADR-0004 §1).
Konsumierende Services konsultieren den Ledger synchron über HTTP; sie bekommen **kein**
`worker-consent`-Shared-Model — Consent ist Business-Daten, nicht transportneutraler Kernel.

**Schichtung (inward-pointing, identisch zu identity-service):**
```
Presentation (HTTP) → Application (Commands/Handler/UoW) → Domain (ConsentEvent, Value Objects, Ports)
                              ← Infrastructure (DB-Models, Repos, Clock, compose) ↗
```

**Wesentliche Abweichung von identity-service:** der Ledger ist **append-only** — es gibt
kein UPDATE/DELETE auf Fakten. Revoke und Delete sind *neue* `consent_events`-Zeilen (logische
Aktion REVOKE/DELETE), nicht Mutation. Das vereinfacht die Infra (kein Optimistic-Locking,
kein Mutable-Aggregat) und erzwingt Audit-correctness strukturell, nicht nur als Konvention.

**Zwei Anwendungsfälle der Zustandsbestimmung:**
- *Write* (GRANT/REVOKE/DELETE): Command → Handler → `ConsentEventRepository.append`
  (append-only) im UoW → Audit synchronous in derselben UoW (ADR-0012-Präzedenz) →
  EventBus-Seite-Effekt (in-process, kein Outbox — Phase 9).
- *Read* (`POST /consent/check`): Query-Handler → Projektion über `consent_events`,
  rechnerisch (SQL `DISTINCT ON`), **unmateriell** — kein zweiter Konsistenzort. Revocation
  wirkt ab dem nächsten Read; in der Praxis echtzeitlich, da der Read-Pfad direkt aus der
  Tabelle liest und kein Cache existiert.

**Read-Pfad-höhe (Entscheid, Ansatz A):** drei Optionen wurden abgewogen:

- **A — HTTP Read-API, Live-Projektion (gewählt).** Synchroner `/consent/check`; aktueller
  Zustand = Projektion aus dem Event-Log; Konsens-Kanon an einem Ort; revocation wirkt ab
  dem nächsten Read. Bijektion zu `identity-service`. Später cache-erweiterbar, falls nötig.
- **B — HTTP-Write + Outbox/Inbox-Events + per-service Projection** (Phase-9-Verzahnung).
  Hot-Path-Read lokal → gut, aber outbox/inbox existiert noch nicht (Phase 9, ADR offen) →
  für Phase 3 Over-Engineering. Wird erst in Phase 9 relevantisiert.
- **C — HTTP-Read + kurzlebiger TTL-Cache im konsumierenden Service.** Dämpft Read-Last →
  gut, aber **bricht** die product-scope-Regel *„revocation must immediately withdraw the
  affected capability"* — bis TTL-Abbruch bleibt die Capability. Gefallen.

A startet konservativ und ist später nach B weiterentwickelbar, wenn Outbox/Inbox steht.

## 3. Komponenten

Spiegel der `identity-service`-Modul-Hierarchie; Domain-Inhalte ersetzt.

### Domain (`apps/consent-service/src/consent_service/domain/`)

- `value_objects.py`
  - `SubjectId` — UUID des Consent-Gebers (Phase 3 lose gekoppelt = identity `user_id` als
    UUID-Typ; kein Domain-Import von identity-service — ADR-0002/0004 boundary).
  - `Capability` — String-Namespace pro Capability-Token, z.B. `"profile.visibility:public"`,
    `"document.attach:application:{id}"`. Regex-validiert (Punkte/Doppelpunkte, nicht leer).
  - `ConsentEventId` — UUID.
  - `Reason` — String, MaxLength-gezügelt; optional bei GRANT, Pflicht bei REVOKE/DELETE.
- `consent_event.py`
  - `ConsentEvent` — Aggregate-Root, append-only. Konstruktion validiert die
    `(action, metadata)`-Allowlist `{"reason","ip","user_agent","actor_id"}` symmetrisch
    zu `identity_service.domain.audit.AuditEvent`; kein PII-Passieren
    (`ConsentMetadataError` bei Exit).
  - Aktionen `GRANT | REVOKE | DELETE` (StrEnum).
  - `actor_id` nullable (self bei GRANT/REVOKE/DELETE; admin-später).
- `services.py`
  - `project_state(events: Sequence[ConsentEvent]) -> ConsentState` (reine Funktion, nie I/O).
    Domain-Regel: GRANT → aktiv; REVOKE → deaktiviert mit Reason; DELETE → logisch gelöscht;
    Duplikat-GRANT → idempotent; Konflikt wird per `(recorded_at, event_id)` geordnet.
    Keine Repo-Abhängigkeit → rein testbar.
- `ports.py`
  - `ConsentEventRepository` (async Port: `append`, `stream(subject)`,
    `latest_effective(subject, capability)`).
  - `Clock` Port (liefert `now()` als UTC; Adapter `SystemClock` in infra).
  - `UnitOfWork` Port.

### Application (`.../application/`)

- `commands.py`
  - `GrantConsentCommand`, `RevokeConsentCommand`, `DeleteConsentCommand`, `CheckConsentQuery`.
  - Jeder Command trägt `actor_id` (Audit-Pflicht); Handler validiert.
- `mediator.py` — pro-Service-Mediator (kein Shared-CQRS-Kernel; wie identity-service).
- `handlers.py`
  - Grant/Revoke/Delete-Handler: konstruieren `ConsentEvent`, persistieren append-only im
    UoW, publishen `ConsentGranted`/`ConsentRevoked`/`ConsentDeleted` Domain-Events via
    EventBus-Seite-Effekt-Seam (ADR-0012-Präzedenz). Check-Handler = reine Projektion-Read,
    kein Schreiben.

### Infrastructure (`.../infrastructure/`)

- `database/models.py` — SQLAlchemy 2.0 async: `consent_events`-Tabelle
  (`id`, `event_id` UUID unique, `subject_id` UUID, `capability` text, `action` text,
  `actor_id` UUID nullable, `reason` text, `metadata` jsonb, `recorded_at` timestamptz).
  Index `(subject_id, capability, recorded_at DESC, event_id DESC)` für die Projektions-Query.
  Check-Constraint `action IN ('GRANT','REVOKE','DELETE')`. Kein UPDATE, kein DELETE im Repo
  angeboten (nur `append`/`stream`/`latest_effective`).
- `database/repositories.py` — `SqlAlchemyConsentEventRepository` implementiert den Port;
  `latest_effective` = `DISTINCT ON (subject_id, capability)` Postgres-Query.
- `clock.py` — `SystemClock` Adapter.
- `database/__init__.py`, `compose.py` — Composition-Root (engine, session_factory, repos,
  mediator, tokens-inject).

### Presentation (`.../presentation/`)

- `compose_api.py` — `build_app(settings)`: ruft `worker_platform.presentation.app.create_api_app`
  (Phase-2-Hook) mit `consent_router` + auth-middleware.
- `http/router.py` — `POST /consent/grant`, `POST /consent/revoke`, `POST /consent/delete`,
  `POST /consent/check` (Query). Bodies = versionierte `worker-contracts`-DTOs, **nicht**
  Domain-Modelle (ADR-0004 §1).

### Cross-cutting (`worker-contracts`)

- Neue `worker-contracts`-Einträge: `ConsentGrantV1`, `ConsentRevokeV1`, `ConsentCheckV1`,
  `ConsentStateV1` (Pydantic-DTOs). Versioniert per V1 (worker-contracts-Schema, ADR-0004 §1).
  Konsumierende Services pin auf V1.

### Auth-Vertrauen

Der auth-middleware wird aus identity-service **ausgegeben Tokens** konsumiert: der Ledger
vertraut dem JWT des Identitäts-Service (HS256 shared Secret via Settings — ADR-0007). Kein
eigner Token-Lifecycle im consent-service. Phase 3 vorerst: jeder authentifizierte User
managed seine eigenen Consents (`actor_id` == `subject_id`); Admin-Delegierung später.

## 4. Datenfluss & Schema

### Write-Pfad (z.B. `POST /consent/revoke`)

1. `compose_api` route → Body = `ConsentRevokeV1` DTO → Command
   `RevokeConsentCommand(subject, capability, actor_id, reason)`.
2. Mediator sendet command → Handler öffnet UoW (`request_scope(session_factory)` wie
   identity-service).
3. Handler konstruiert `ConsentEvent.revoke(...)` — Domain validiert Metadaten-Allowlist
   (wirft `ConsentMetadataError` bei PII-Pass-through).
4. `ConsentEventRepository.append(event)` — INSERT, kein UPDATE.
5. **Synchronous Audit-Ereignis** in derselben UoW (`audit_events`-Tabelle im
   consent-service; identischer `AuditEvent`-Typ + `AuditRepository.append` aus ADR-0012).
   Consent-Schreib + Audit in einer Transaktion — success/failure-atomicity.
6. UoW commit → `ConsentRevoked` Domain-Event via `EventBus.publish` als Side-Effect-Seam
   (nicht audit; audit bereits persistiert). EventBus = rein in-process (kein Outbox, Phase 9).
7. Response → `ConsentStateV1`-DTO = projizierter Zustand nach dem append.

### Read-Pfad (`POST /consent/check`)

1. Body `ConsentCheckV1 {subject, capability}` → Query `CheckConsentQuery`.
2. Handler ruft `ConsentEventRepository.latest_effective(subject, capability)`.
3. Single Postgres-Fensterabfrage (rechnerisch, kein Status-Tisch):

```sql
SELECT DISTINCT ON (subject_id, capability)
       id, subject_id, capability, action, recorded_at, event_id
FROM   consent_events
WHERE  subject_id = $1 AND capability = $2
ORDER  BY subject_id, capability, recorded_at DESC, event_id DESC
```

→ letztes effektives Event pro `(subject, capability)` (recent by `recorded_at`, Tiebreaker
`event_id` DESC, deterministisch). Handler deutet: `GRANT` → `{granted: true}`; `REVOKE` →
`{granted: false, reason}`; `DELETE` → `{granted: false, deleted: true}` (logisch gelöscht,
Capability deaktiviert; Fadenreihe rest bleibt für Audit-Rückhaltbarkeit). Kein Treffer →
`{granted: false, reason: "no consent event"}` (Negativ ist Zustand, kein Fehler).

### Schema (`migrations/versions/0001_init_consent.py`, Mechanismus identisch zu identity-service)

```
consent_events
─────────────────────────────────────────────
id              bigint identity pk
event_id        uuid not null unique   -- domain-side ID (idempotency)
subject_id      uuid not null
capability      text not null
action          text not null          -- 'GRANT'|'REVOKE'|'DELETE' (check constraint)
actor_id        uuid null              -- who caused event (self or admin)
reason          text null              -- required body for REVOKE/DELETE
metadata        jsonb not null default '{}'
recorded_at     timestamptz not null
─────────────────────────────────────────────
CREATE UNIQUE INDEX ux_consent_events_event_id ON consent_events(event_id);
CREATE INDEX ix_consent_events_lookup
  ON consent_events(subject_id, capability, recorded_at DESC, event_id DESC);
ALTER TABLE consent_events
  ADD CONSTRAINT ck_consent_events_action
  CHECK (action IN ('GRANT','REVOKE','DELETE'));
```

`audit_events` co-migriert in derselben `0001`-Migration (selbige Spalten wie
`identity_service` `audit_events`: `id`, `actor_id` nullable, `action`, `metadata` jsonb
allowlist, `recorded_at`); das consent-service audit ist **service-owned** wie ADR-0012 es
für identity-service festlegt — kein geteilter Audit-Kanon.

### Idempotenz & Duplikate

- `event_id uuid unique` → Server/Client-Wiederholung mit gleichem `event_id` schlägt am
  Unique-Constraint an; Handler kann `IntegrityError` auf `event_id` als Idempotenz-Rückkehr
  fangen und den aktuellen projizierten Zustand ohne neues Fact liefern (optional, nicht
  blockierend).
- `latest_effective`-Projektion sortiert per `(recorded_at DESC, event_id DESC)`; bei
  Duplicate-GRANT ist der spätere `recorded_at` dominant (Tiebreaker `event_id`).
- `GRANT` nach `REVOKE` ist erlaubt (Re-Consent nach Widerruf) → neuer Fact-Timestamp >
  dem Revoke → Projektion aktiv. Keine Zustandsperre (keine „Pause nach Revoke"-Policy in
  Phase 3; später optional).

## 5. Fehlerbehandlung

Symmetrisch zu identity-service — RFC-9457-Problem-Antworten
(`worker_platform.presentation.errors.register_exception_handlers` via `create_api_app`);
Domain-Errors als Value-Objects; HTTP-Mapping einheitlich pro Klasse.

### Domain-Errors

- `ConsentMetadataError` — Metadaten-Schlüssel außerhalb `{"reason","ip","user_agent","actor_id"}`
  → HTTP 400 `{"detail":"complementary metadata not allowed"}`.
- `InvalidConsentActionError` — kein GRANT/REVOKE/DELETE → HTTP 400. (Check-Constraint ist
  DB-Rückfall, Domain validiert zuerst.)
- `ConsentSubjectMismatch` — authentifizierter `actor_id` ≠ `subject_id` und kein
  Rechte-Modell → HTTP 403. Strikte Eigenverwaltung; `/check` erlaubt jeden
  authentifizierten Aufruf (Enabler für konsumierende Services, die für ein Subjekt prüfen).
- `ConsentConflictError` — (Zukunft, bei erweiterter Logik) → HTTP 409. Phase 3 vorerst
  nicht ausgelöst (append-only hat keine echten Konflikte).

### HTTP-Mapping

| Domain-Error | HTTP | `detail` |
|---|---|---|
| `MalformedBody` | 422 | `"invalid request body"` |
| `ConsentMetadataError` | 400 | `"complementary metadata not allowed"` |
| `InvalidConsentActionError` | 400 | `"invalid consent action"` |
| `ConsentSubjectMismatch` | 403 | `"subject mismatch"` |
| `Unauthorized` (Principal=None) | 401 | `"not authenticated"` |
| `ConsentConflictError` (Zukunft) | 409 | `"consent conflict"` |

### Audit-atomicity

Audit-Schreiben läuft **in derselben UoW-Transaktion** wie der Consent-Ereignis-Schreib
(ADR-0012-Präzedenz). Kommt es nach dem Persistieren des Consent-Ereignisses zu einem Fehler
(z.B. EventBus-Publish wirft) → Transaktion rollt zurück → **keine verwaisten Audit-Zeilen**,
kein halber Zustand. EventBus-Publish-Seam ist Side-Effect nach dem Commit; Phase 3 EventBus =
rein in-process.

### Check-Query-Fehler

`/check` ist read-only, kein UoW-Schreiben. `subject_id` nicht gefunden → kein 404, sondern
`{granted: false, reason: "no consent event"}`. Negativ ist Zustand, kein Fehler (Enabler).

### Revocation-Effektivität

Ansatz A — Revocation wirkt ab dem nächsten Read. Schreib-Commit ist synchron und die
Projektion liest live direkt aus `consent_events`, also ist eine `REVOKE`-Operation sofort
nach Commit sichtbar für jedes folgende `POST /consent/check`. Kein Cache (Ansatz C wegen
Regelbruch entfallen).

## 6. Tests

Symmetrisch zu identity-service: Unit (pure Domain, kein I/O) + Integration
(Testcontainers-Postgres, Docker-abhängig, ADR-0011 offline-skip) + App (`/health/live`,
keine DB). `pytest-asyncio` auto-mode; `make check` pro Sub-Task.

### Unit (`tests/unit/`)

- `test_consent_event.py` — GRANT/REVOKE/DELETE-Konstruktion ✓; Metadaten-Allowlist wirft
  `ConsentMetadataError` bei PII/Unbekanntem; `actor_id` nullable; `email`/`password`/
  `tokens` niemals im Event-Body (PII-Test symmetrisch zu `test_audit.py`).
- `test_projection.py` — reine Funktion `project_state(events)`:
  GRANT nach leerem Zustand → aktiv; GRANT→REVOKE → deaktiviert mit Reason; GRANT→REVOKE→GRANT
  → aktiv (Re-Consent); DELETE nach GRANT → logisch-gelöscht; Duplicate-GRANT (gleiches
  `recorded_at`) → Tiebreaker `event_id` DESC; `recorded_at`-Ordnung dominant.
- `test_value_objects.py` — `SubjectId` (UUID-Validität), `Capability` (Namespace-Regex),
  `Reason` (MaxLength; optional bei GRANT; Pflicht bei REVOKE/DELETE).

### Integration (`tests/integration/`, Testcontainers-Postgres; `_docker.py`-shared; `pytestmark=skipif`)

- `test_migrations.py` — `alembic upgrade head` erstellt `consent_events` + `audit_events`;
  Verifikation der Check-Constraint.
- `test_repository_roundtrip.py` — append/stream; `latest_effective` Fensterabfrage;
  Idempotizität (`event_id` unique); Projektionen GRANT→REVOKE→GRANT.
- `test_consent_endpoints.py` — `POST /consent/grant|revoke|delete|check` via `TestClient`;
  200/401/403/400-Pfade; `/check` nach revoke → `granted:false`.
- `test_audit_atomicity.py` — Grace fail (z.B. `ConsentMetadataError`) persistiert Audit
  nicht; erfolgreich = Audit-Zeile da.
- `test_eventbus_seam.py` — `ConsentGranted`/`ConsentRevoked`/`ConsentDeleted` Domain-Events
  werden gepublisht (Side-Effect-Seam symmetrisch zu ADR-0012).

### App (`tests/test_app.py`)

- `/health/live` antwortet, X-Correlation-ID, Security-Header (aus der Plattform geliefert).

### Cross-Contract (`packages/worker-contracts/tests/`)

- Smoke-Test: `Consent{Grant,Revoke,Check,State}V1`-Pydantic-Modelle → Version-Stabilität
  (ADR-0004 §1).

### 4-Gate-Sicherung

`make check` (ruff format → ruff check → mypy → pytest fail-fast) pro Sub-Task + Committakt
(Phase-2-Konvention). Frontend-Tests entfallen hier (reiner Backend-Service).

## 7. ADR (in Sub-step 3.1 neu zu schreiben)

- **ADR-0013 — Consent-Ledger: standalone append-only service.**
  - *Context:* consent is business data + enabler (ADR-0004 §3); sharing rule verbietet
    shared DB / cross-service repo (CLAUDE.md, ADR-0004 §1); product-scope fordert immediate
    revocation on withdrawal.
  - *Decision:* `apps/consent-service` als standalone service (identity-service-Spiegel),
    append-only `consent_events` (GRANT/REVOKE/DELETE als neue Fakten, kein UPDATE), rechnerische
    Projektion (unmateriell — kein Status-Tisch, kein zweite Konsistenzort), synchroner HTTP-Read
    (Ansatz A — kein TTL-Cache, weil regelnimmediate-revocation brächen).
  - *Consequences:* Konsens-Kanon an einem Ort; GDPR/Retention an einem; consuming services
    ziehen synchron am Ledger; Outbox/Inbox + per-service Projection ist Phase-9-Upgrade
    (replace synchronous read + projector), Konsistenz-Modell upgrading then. JWT-Vertrauen aus
    identity-service (HS256 shared secret) — consent-service issued keine eigenen Tokens.

## 8. DoD (Sub-step 3.1)

- `apps/consent-service` läßt via `worker-consent` Console-Entrypoint und `/health/live`.
- `alembic upgrade head` erstellt `consent_events` + `audit_events` (`test_migrations.py`).
- `POST /consent/grant|revoke|delete` schreibt append-only + Audit atomic;
  `POST /consent/check` projektiert; revocation sofort sichtbar.
- PII-Allowlist erzwungen (`test_consent_event.py`); Domain-Event-Publish-Seam getestet.
- `worker-contracts` DTOs V1 versioniert; Smoke-Test pinnt Version.
- ADR-0013 geschrieben.
- `make check` grün; ROADMAP Phase-3-Status: Sub-step 3.1 ✅.
