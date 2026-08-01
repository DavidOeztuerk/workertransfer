# Profile-Service — Arbeitsstand

**Branch:** `profile-service` (von `docs-status-and-cleanup`, das auf `phase-3-consent-ledger` aufsetzt)
**Wiederaufnahme:** diese Datei lesen, dann bei der ersten unerledigten Aufgabe weitermachen.
**Validierung jederzeit:** `./scripts/validate.sh`

## Entschieden (Brainstorming 01.08.2026)
- Umfang 3.2: Profil + Consent-Gate. Dokumente = 3.5, strukturierte Erfahrung = 3.3.
- Eine Capability: `profile.visibility:public`, ganz oder gar nicht.
- Lesen fremder Profile nur mit aktivem Tenant (Unternehmen).
- Unternehmen sieht ALLE freigegebenen Profile, keine Freigabe pro Firma.
  Der Scout (Phase 6) nutzt dasselbe Gate, ist kein zweiter Kanal.
- Inhalt: headline, bio, location, remote_ok, skills[].
- Lesepfad: Einzelabruf + Liste (Seite 20, parallele Checks). Kein Bulk, kein Cache (ADR-0013).
- Ohne Consent: `404`, nicht `403` — „versteckt" muss von „gibt es nicht" ununterscheidbar sein.
- Eine Seite liefert ggf. weniger als 20 Einträge; das ist beabsichtigt.
- Profile-Service schreibt NIE Consent, er liest nur.

## Aufgaben
- [x] 0 Spec schreiben + committen
- [x] 1 Generator reparieren (fehlende base.py, Docker-Templates raus, Import-Test)
- [x] 2 profile-service via `worker new-service` erzeugen + Workspace/Compose einhängen
- [x] 3 Domäne: Profile-Aggregat + Wertobjekte (TDD)
- [x] 4 Migration 0001 + Modelle + Repository
- [ ] 5 ConsentGate-Port + HTTP-Adapter (TDD)
- [ ] 6 Handler: eigenes Profil schreiben/lesen (TDD)
- [ ] 7 Handler: Fremdabruf + Liste mit Consent-Gate (TDD)
- [ ] 8 Contracts-DTOs + HTTP-Endpunkte
- [ ] 9 Integrationstests (Docker) inkl. Widerruf wirkt sofort
- [ ] 10 Frontend: Profil bearbeiten + Sichtbarkeit schalten (TDD)
- [ ] 11 Frontend: Kandidatenliste für Unternehmen (TDD)
- [ ] 12 Playwright-E2E, self-skip ohne laufenden Stack
- [ ] 13 scripts/validate.sh — ein Befehl, der alles prüft und den Stand meldet
- [ ] 14 ADR-0020 + ROADMAP/CLAUDE.md nachziehen

## Log
- 0 Spec: docs/superpowers/specs/2026-08-01-profile-service-design.md
- 1 Generator repariert (Commit folgt). Gefunden: base.py wurde nie geschrieben;
  das Beispielmodell war doppelt kaputt (`postgresql_where` auf UniqueConstraint,
  Lambda auf undefinierten Namen) — beides unsichtbar für `ast.parse`.
  Neuer Test importiert jetzt `<service>.main`, nicht nur die Modelle.
  Docker-Templates je Service entfernt; der Generator druckt stattdessen den
  Compose-Block. 353 passed / 3 skipped.
- 2 profile-service erzeugt und eingehängt (Workspace, initdb `profile`, Compose :8003).
  Der Generator brauchte dafür VIER weitere Reparaturen, alle vorher unsichtbar:
  (a) pyproject deklarierte 20 Pakete inkl. gelöschter (worker-cqrs/-exceptions),
      `uv sync` scheiterte daran;
  (b) database/__init__.py brachte eine ZWEITE Base, Mixins, DatabaseSettings und
      eine UnitOfWork mit — Duplikate von base.py und worker_database;
  (c) domain/application/__init__ verwiesen auf gelöschte Module;
  (d) 30 leere Verzeichnisse, Beispiel-Entity/Repository/Mediator, alle rot.
  Neu: Gate-Test prüft, dass generiertes src/ ruff+format besteht.
  pytest läuft jetzt im importlib-Modus — sonst kollidieren drei
  `tests/test_app.py` über die Servicegrenzen hinweg.
  357 passed / 3 skipped.
- 3 Domäne: Profile-Aggregat + Skills-Wertobjekt, 17 Unit-Tests.
  Skills entdoppelt case-insensitiv VOR der Mengenprüfung (31x "Python" ist eine
  Fähigkeit, kein Fehler). update() prüft alles, bevor es irgendetwas schreibt.
- 4 Migration 0001 + Modelle + Repository, 6 Integrationstests gegen Postgres.
  Cursor trägt (updated_at, id) — mit dem Zeitstempel allein überspringt das
  Blättern Profile, die in derselben Sekunde geändert wurden. Kaputter Cursor
  = Anfang, kein 400.
