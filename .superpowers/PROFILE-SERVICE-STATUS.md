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
- [ ] 1 Generator reparieren (fehlende base.py, Docker-Templates raus, Import-Test)
- [ ] 2 profile-service via `worker new-service` erzeugen + Workspace/Compose einhängen
- [ ] 3 Domäne: Profile-Aggregat + Wertobjekte (TDD)
- [ ] 4 Migration 0001 + Modelle + Repository
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
