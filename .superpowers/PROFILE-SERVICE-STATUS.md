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
- [x] 5 ConsentGate-Port + HTTP-Adapter (TDD)
- [x] 6 Handler: eigenes Profil schreiben/lesen (TDD)
- [x] 7 Handler: Fremdabruf + Liste mit Consent-Gate (TDD)
- [x] 8 Contracts-DTOs + HTTP-Endpunkte
- [x] 9 Integrationstests (Docker) inkl. Widerruf wirkt sofort
- [x] 10 Frontend: Profil bearbeiten + Sichtbarkeit schalten (TDD)
- [x] 11 Frontend: Kandidatenliste für Unternehmen (TDD)
- [x] 12 Playwright-E2E, self-skip ohne laufenden Stack
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
- 5 ConsentGate-Port + HttpConsentGate (9 Tests). Fail closed bei Ausfall,
  auch bei 401/500 — ein `False` wäre eine Aussage über die Person.
- 6/7 Handler (41 Unit-Tests gesamt). Fund: worker_core.Result kann kein None
  als Erfolgswert tragen (.value wirft) — handle_get_my_profile gibt deshalb
  direkt Profile|None zurück statt Result. Dieselbe Falle steckt latent in
  identity's handle_register (Result.ok(None)), dort liest nur niemand .value.
- 8 Contracts (ProfileV1/SaveProfileV1/ProfilePageV1), Router, Composition-Root,
  7 App-Tests. Nebenbei: worker_auth exportiert jetzt get_request_user — bis
  dahin schrieb jeder Service seine eigene Fassung (identity eine Funktion,
  consent ein getattr mitten im Router). 408 passed / 3 skipped.
- 9 Integrationstests gegen echte Dienste. Der erste Lauf fand sofort einen
  Fehler, den 41 Unit-Tests nicht sehen konnten: der Router las
  `principal.user_id`, der Prinzipal heißt aber `TokenPayload.sub`. Keine
  Unit-Test-Fassung hatte den Router je über die echte Middleware aufgerufen.
  Drei Fixture-Runden bis grün: DROP DATABASE gegen belegte Pools, dann
  asyncpg-Pools an der Event-Schleife des ersten Tests. 413 passed / 3 skipped.
- 10 Frontend TDD: profile/client.ts (10 Tests), Route /profile (9 Tests),
  Switch + TextArea im UI-Paket (6 Tests). Der Ladezustand kam aus einem
  fehlschlagenden Test, nicht aus Politur: das Formular rendert nicht mehr leer
  und füllt sich nach — wer in der Lücke tippt, verliert sonst die Eingabe.
  Je Dienst eine eigene VITE_-Basis-URL (8001/8002/8003). 66 Frontend-Tests.
- 11 Kandidatenliste: listCandidates (5 Tests) + Route (7 Tests). Vier
  unterscheidbare Fehlergründe statt einer Meldung, weil die Seite auf jeden
  anders antwortet. 503 zeigt nichts statt einer leeren Liste — eine leere Liste
  wäre eine Behauptung über Menschen. Keine Gesamtzahl: sie verriete, wie viele
  gerade NICHT freigegeben sind. 78 Frontend-Tests.
- 12/13 Playwright-E2E (Selbst-Skip nach ADR-0011 verifiziert) und
  scripts/validate.sh. Beim ersten echten Lauf zwei Betriebsfallen gefunden:
  scripts/initdb läuft nur bei leerem Volume (profile-service drehte sich in
  "database profile does not exist"), und der Web-Container startet nicht, wenn
  sich die Lockfile geändert hat — pnpm will node_modules löschen und bricht
  ohne TTY ab. Beides im Compose dokumentiert bzw. behoben (CI=true).
