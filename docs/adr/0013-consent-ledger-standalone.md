# ADR-0013: Consent-Ledger als eigenständiger, append-only Service

Date: 2026-07-31
Status: Accepted
Related: ADR-0004 §3 (Consent als Enabler), ADR-0012 (Audit synchron in-UoW), ADR-0016 (eigene MetaData je Service), [Design-Spec](../superpowers/specs/2026-07-26-phase-3-substep-3.1-consent-ledger-design.md)

## Kontext

`docs/product-scope.md` und ADR-0004 §3 machen Consent zum **Enabler**: Profil-
Sichtbarkeit, Dokumentanhänge, Arbeitgeber-Kontakt, GitHub-Import und jede
AI-Analyse fragen zuerst den Ledger. Zwei Anforderungen daraus sind hart:

1. *„Revocation muss die betroffene Capability sofort zurückziehen."*
2. Consent ist Business-Daten und Auditgegenstand — keine geteilte Datenbank,
   kein Cross-Service-Repository (ADR-0004 §1).

## Entscheidung

**`apps/consent-service` ist ein eigenständiger Service** in der Form von
`identity-service`, mit eigener Datenbank und eigener `MetaData` (ADR-0016).
Konsumierende Services fragen ihn synchron über HTTP; es gibt **kein**
`worker-consent`-Shared-Model — Consent ist Domäne, nicht transportneutraler Kernel.

**Der Ledger ist append-only.** GRANT, REVOKE und DELETE sind *neue Fakten*, keine
Mutationen. Das ist strukturell erzwungen, nicht per Konvention:
`ConsentEvent` ist `frozen`, und `ConsentEventRepository` bietet **kein** `update`
und **kein** `delete` — es existiert schlicht keine Methode, die Historie
umschreiben könnte. Ein Test prüft die Abwesenheit dieser Methoden.

**Der Zustand wird berechnet, nicht gespeichert.** Es gibt keine Status-Tabelle.
`project_state()` ist eine reine Funktion über den Event-Strom und definiert die
Regeln; `latest_effective()` führt dieselbe Reduktion als Postgres-`DISTINCT ON`
für den Hot-Path aus. Damit gibt es genau **einen** Ort, an dem Consent wahr ist,
und keinen zweiten Speicher, der auseinanderlaufen kann.

**Read-Pfad: Ansatz A (synchroner HTTP-Read, Live-Projektion).** Drei Optionen
wurden abgewogen:

- **A (gewählt):** `/consent/check` liest live aus `consent_events`. Ein REVOKE ist
  ab dem nächsten Read sichtbar — in der Praxis sofort, weil kein Cache existiert.
- **B (Outbox/Inbox + Projektion je Service):** besserer Hot-Path, aber Outbox/Inbox
  existiert noch nicht (Phase 9). Für Phase 3 Over-Engineering; A ist später
  dorthin erweiterbar.
- **C (TTL-Cache im konsumierenden Service):** **verworfen** — bricht die
  Sofortwirkung. Bis zum TTL-Ablauf bliebe die Capability aktiv. Ein Cache ist hier
  kein Performance-Detail, sondern ein Regelbruch.

**Ordnung ist `(recorded_at, event_id)`.** Zwei Fakten im selben Clock-Tick lösen
sich deterministisch auf, statt von der Einfügereihenfolge abzuhängen.

**Re-Consent nach Revoke ist erlaubt.** Ein Widerruf darf für die betroffene Person
keine Einbahnstraße sein. Eine Sperrfrist wäre eine Produktentscheidung und wurde
bewusst nicht eingebaut.

**Abwesenheit ist ein Zustand, kein Fehler.** `/check` auf ein nie berührtes Paar
liefert `200` mit `{granted: false, reason: "no consent event"}`, kein 404 —
konsumierende Services müssen über beliebige Capabilities fragen können.

**Selbstverwaltung in Phase 3.** `actor_id` muss `subject_id` entsprechen; ein
Delegations-/Admin-Modell ist eine spätere, ausdrückliche Entscheidung und nicht
etwas, das durch Unterlassen entsteht. `/check` steht dagegen jedem
authentifizierten Aufruf offen — genau das macht den Ledger als Enabler nutzbar.

**Audit synchron in derselben UoW** (ADR-0012-Präzedenz): Consent-Fakt und
Audit-Zeile committen gemeinsam. Kein „Consent gespeichert, Audit verloren", keine
verwaiste Audit-Zeile. Die Metadaten-Allowlist lässt den *Capability-Namen* zu,
niemals die Daten, die die Capability betrifft.

## Konsequenzen

- GDPR-relevante Vorgänge (Auskunft, Löschung, Consent-Historie) haben genau eine
  Anlaufstelle.
- `DELETE` zieht die Capability logisch zurück; die Faktenkette bleibt für die
  Auditierbarkeit erhalten. Ein echtes „Recht auf Vergessen" (physisches Löschen)
  ist ein separater Retention-Schritt und **nicht** Teil dieses Slices.
- Jeder konsumierende Service zieht synchron am Ledger. Der Upgrade-Pfad auf
  Ansatz B ersetzt in Phase 9 den Read, nicht das Schreibmodell.
- Der Ledger stellt keine eigenen Tokens aus (ADR-0015).

## Verifikation

- `tests/unit/test_projection.py` — jede Projektionsregel einzeln, inkl. Tiebreaker
  und Re-Consent.
- `tests/integration/test_repository_roundtrip.py` — Revoke sofort sichtbar,
  Capability-Isolation, `event_id`-Idempotenz, Historie bleibt erhalten, kein
  Mutations-API.
- `tests/integration/test_consent_endpoints.py` — grant → check → revoke → check
  über HTTP; 403 bei fremdem Subject; Audit-Atomicity; PII-freie Audit-Metadaten.
