# ADR-0012: Audit persistiert synchron in-UoW (PII-Allowlist); EventBus = Side-Effect-Naht

Date: 2026-07-20
Status: Accepted
Supersedes: —
Related: ADR-0002 (worker-platform = Kernel), ADR-0004 (versionierte Contracts, kein Scraping, Consent-first), ADR-0003 (Composition-Root), ADR-0011 (Integration-Testcontainers)

## Kontext

`worker_events` liefert einen In-Process-`EventBus`ohne Persistenz (nur Dispatch nach `event_type.__name__`). Phase 2 muss Audit-Events für sicherheitsrelevante Aktionen (Register/Login/Refresh/Revoke) persistieren — DoD: jede sicherheitsrelevante Aktion wird als PII-freies Audit-Event in derselben Transaktion wie der Befehl gespeichert —,ohne Consent-Ledger-PII zu leaken (Consent ist ein Phase-3-Anliegen). Outbox/Inbox ist ULTRAPLAN Phase 9, nicht jetzt.

Vorgefunden (Sub-step 2.5, Task 17): die Befehle persistieren `AuditEvent`s bereits **synchron innerhalb des Befehls-UoW** (`SqlAlchemyAuditRepository.append` in der UoW-Session, commitet gemeinsam mit dem User-/Session-/Token-Write) und *danach* publizieren sie die Domain-Events `UserRegistered`/`UserLoggedIn` via `await deps["eventbus"].publish(ev)`. Audit war also nie über den EventBus geroutet; die EventBus-Publikation existiert, hatte aber (vor Task 22) keinen Produktionssubskriptor.

Daneben: ein Typ-Split zwischen `worker_core.DomainEvent` (dessen Subklassen `UserLoggedIn`/`UserRegistered` sind) und `worker_events.DomainEvent` (gegen das `EventBus.subscribe`/`publish` typisiert waren). Nominal inkompatibel → mypy-strict-Reject beim Subskribieren der Identitäts-Domain-Events, obwohl die Dispatch rein namensbasiert ist.

## Entscheidung

`AuditEvent` ist ein **service-owned** Domain-Typ in `identity_service.domain.audit` (nicht im geteilten `worker_events`; ADR-0004 — Audit-Payload ist service-spezifisch). Sein `metadata` ist eine **validierte Allowlist** (`AUDIT_METADATA_ALLOWLIST = {"reason", "ip", "user_agent"}`); Konstruktion mit einem anderen Schlüssel wirft `AuditMetadataError`.

**Audit-Persistenz ist synchron innerhalb derselben UoW-Transaktion** wie der Sicherheitsbefehl → Atomicity (Login + Audit bestehen oder scheitern gemeinsam; `test_audit_atomicity.py` verifiziert `register`+`login_success` bzw. `login_failure`). Der in-process `EventBus` publiziert `UserRegistered`/`UserLoggedIn` als **Side-Effect-Naht** (kein Audit-Republish — Audit ist bereits persistiert). `actor_id` ist nullable (`None` bei unbekanntem User zur fehlgeschlagenen Login-Versuch). `audit_events` werden **nicht** kaskaden-gelöscht mit Users (Retention).

**EventBus-Typ-Signatur gelockert** (Task 22): `subscribe(event_type: type, ...)` / `publish(event: object)` / `publish_all(events: list[object])` statt `type[DomainEvent]`/`DomainEvent`. Der Bus dispatched ausschließlich nach `event.__class__.__name__` und liest keine Event-Attribute — die `DomainEvent`-Annotation war eine *aspirationelle* Über-Konstriktion, die reale Caller (via `Any`-Indirektion in `commands.py`) ohnehin umgingen. Eine Service-Domain-Event-Hierarchie (`worker_core.DomainEvent`-Subklassen) kann nun ohne nominale Import-Abhängigkeit auf `worker_events.DomainEvent` subskribieren/publizieren. (Der tiefere, korrekte Fix — `worker_core.DomainEvent` und `worker_events.DomainEvent` zu *einem* Kanon-Typ zu verschmelzen — ist ADR-0005/ADR-0002-Kanon-Territorium und out-of-scope für Phase 2.)

## Upgrade-Pfad dokumentiert

Outbox (Phase 9) kann den synchronen `AuditRepository.append`-Aufruf durch eine Outbox-Zeilen-Insert + asynchrone Worker ersetzen — der `AuditEvent`-Typ und die `metadata`-Allowlist bleiben unverändert; nur der Persistenzmechanismus wechselt. Die Side-Effect-Naht (EventBus) bleibt davon unberührt.

## Konsequenzen

- Audit trägt niemals Passwort, E-Mail, Consent-Payloads oder Tokens (durch `AuditEvent`-Konstruktion erzwungen, von `test_audit.py` verifizirt). Consent (Phase 3) kann nicht versehentlich in Audit leaken.
- Atomicity: Audit-Write und Befehls-Write sind eine UoW-Transaktion; kein "Befehl ok, Audit verloren" und kein "Audit ohne Befehl".
- Der EventBus ist kein Audit-Kanal — er ist eine Side-Effect-Naht für zukünftige domänenübergreifende Reaktionen (Benachrichtigungen, etc.). Produktionssubskriptor (Task 22) ist ein No-op-Handler (`_noop_domain_event_handler`).
- Retention/Anonymisierung der `audit_events` ist ein späterer GDPR-Schritt (out-of-scope).

## Verifikation

- `apps/identity-service/tests/unit/test_audit.py` — PII-Allowlist (`AuditMetadataError` bei nicht-Erlaubt-Schlüsseln; keinerlei Passwort/E-Mail/Token in Audit-Payload).
- `apps/identity-service/tests/integration/test_audit_atomicity.py` — nach erfolgreichen Register+Login: `audit_events` enthält `register` + `login_success` (Atomicity in-Einer-UoW); nach fehlgeschlagenem Login (unbekannter User): `login_failure` mit `actor_id IS NULL`.
- `apps/identity-service/tests/unit/test_eventbus_seam.py` — `compose_infrastructure` subskribiert produktiv einen Handler für `UserLoggedIn` + `UserRegistered` (die Naht existiert und ist injizierbar für Tests).
- `make check` grün (111 passed, 2 skipped nach Task 22).
