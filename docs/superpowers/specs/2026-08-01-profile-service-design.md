# Profile-Service (Design)

- **Status:** Design (brainstorming-approved, pre-implementation)
- **Date:** 2026-08-01
- **Slice:** ULTRAPLAN Phase 3, Sub-step 3.2
- **Relates:** ADR-0004 (eigene DB je Service), ADR-0009/0017/0018 (Tenant), ADR-0010 (Alembic je Service), ADR-0011 (Docker offline-skip), ADR-0013 (Consent-Ledger, Ansatz A: synchron, kein Cache), ADR-0015 (geteilte JWT-Middleware), ADR-0016 (eigene MetaData), `docs/product-scope.md`, `docs/vision/kon.txt` (Regel Nr. 1)

## 1. Ziel & Scope

Der Consent-Ledger steht seit 3.1 — und wird von niemandem benutzt. Ein Enabler
ohne Konsumenten ist eine Behauptung. Dieser Slice liefert den ersten echten
Lesepfad, der ihn befragt, bevor er etwas zeigt.

Zugleich ist er der erste Einlöseversuch von `kon.txt` Regel Nr. 1: *kein
Microservice, bevor die Plattform einen erzeugen kann.* Der Service entsteht aus
`worker new-service`, nicht aus Copy-Paste.

**In-Scope:** Generator-Reparatur; `apps/profile-service`; Profil anlegen und
pflegen; Fremdabruf und Liste, beides consent-gegated; Frontend zum Bearbeiten
und Schalten der Sichtbarkeit sowie eine Kandidatenliste für Unternehmen;
Playwright-E2E; ein Validierungsskript.

**Out-of-Scope:** Dokumente und Uploads (3.5, `worker-files`/`worker-storage`);
strukturierte Berufserfahrung (3.3, Lebenslauf); Suche und Matching (Phase 4);
Skill-Graph aus GitHub (Phase 6); Kontaktaufnahme und Marktstatus (Phase 4/5);
Bulk-Consent-Endpunkt (erst wenn die Liste tatsächlich zu langsam wird).

## 2. Vorarbeit: der Generator

`worker new-service` erzeugt heute Code, der **parst, aber nicht importiert** —
`models.py` importiert `…infrastructure.database.base`, und diese Datei wird nie
geschrieben. Durchgerutscht ist das, weil `test_generated_python_parses` nur
`ast.parse` aufruft: Syntax geprüft, Importierbarkeit nicht.

Drei Reparaturen vor dem ersten Profil:

1. `base.py` mit ausgeben — service-eigene `DeclarativeBase` plus Mixins (ADR-0016).
2. Die Templates für `Dockerfile` und `docker-compose.yml` entfernen. Es gibt
   ein geteiltes `docker/service.Dockerfile` und eine Root-Compose; ein
   generierter Service brächte sonst konkurrierende Infrastruktur mit. Der
   Generator druckt stattdessen den Compose-Block aus, der einzufügen ist.
3. Ein Test, der den generierten Service **importiert** statt ihn nur zu parsen.

## 3. Architektur

`apps/profile-service` spiegelt die Form von identity- und consent-service:
eigene Datenbank `profile` (ADR-0004), eigene Alembic-Historie (ADR-0010),
eigene `MetaData` (ADR-0016), Token-Verifikation über
`worker_auth.JwtAuthMiddleware` (ADR-0015), Tenant aus dem Claim über
`ClaimTenantResolver` (ADR-0009).

Neu ist genau ein Baustein: **der Consent-Client.**

- Port `ConsentGate` in der Application-Schicht:
  `async def may_see(subject_id: UUID, *, bearer: str) -> bool`
- HTTP-Adapter `HttpConsentGate` in der Infrastruktur, spricht
  `POST /consent/check` mit den DTOs aus `worker-contracts`
  (`ConsentCheckV1`, `ConsentCheckResultV1`).
- Kein `worker-consent-client`-Paket: Consent ist Domäne, kein
  transportneutraler Kernel (Sharing-Rule).

Der Adapter reicht das **Token des anfragenden Nutzers** durch. `/consent/check`
verlangt einen authentifizierten Aufrufer und antwortet jedem Angemeldeten; ein
eigener Service-Account wäre ein zweiter Vertrauensweg ohne Gewinn.

## 4. Datenmodell

Ein Profil pro Person, und die Person **ist** der Schlüssel — `profiles.id` ist
die `subject_id` aus dem Token. Ein eigener Profil-Identifikator erzwänge eine
Zuordnungstabelle und verkomplizierte die Consent-Abfrage, die ohnehin über
`subject_id` läuft.

```
profiles                        (Datenbank `profile`, Migration 0001)
  id           uuid  pk         -- = subject_id, kein eigener Schlüssel
  headline     text  not null   -- „Senior Python Backend"
  bio          text  not null   -- Freitext, darf leer sein
  location     text  not null   -- Ort/Region, darf leer sein
  remote_ok    boolean not null default false
  skills       jsonb not null default '[]'
  created_at / updated_at  timestamptz not null
```

Grenzen (Domäne, nicht nur DB): `headline` 1–120 Zeichen, `bio` ≤ 4000,
`location` ≤ 120, `skills` ≤ 30 Einträge à ≤ 50 Zeichen, keine Duplikate,
leere Einträge werden verworfen.

Skills bleiben eine schlichte Liste. Der Skill-Graph aus verifizierten
GitHub-Signalen (Phase 6) ersetzt diese Selbstauskunft nicht, er ergänzt sie —
eine Normalisierung jetzt hieße, zwei Strukturen zu pflegen.

## 5. Endpunkte

| Methode | Pfad | Wer | Consent |
|---|---|---|---|
| `PUT` | `/profiles/me` | angemeldet | nein — es ist das eigene |
| `GET` | `/profiles/me` | angemeldet | nein |
| `GET` | `/profiles/{subject_id}` | aktiver Tenant | **ja** |
| `GET` | `/profiles?cursor=&limit=` | aktiver Tenant | **ja, je Eintrag** |

`PUT` statt `PATCH`: die Oberfläche bearbeitet das ganze Formular. Ein
Teil-Update müsste je Feld „nicht gesetzt" von „auf leer gesetzt" unterscheiden —
Aufwand ohne Nutzen, solange es ein Formular ist.

**Profile-Service schreibt niemals Consent.** Erteilen und Widerrufen laufen
direkt gegen consent-service; die Oberfläche ruft dort an. Sonst gäbe es zwei
Stellen, an denen Einwilligung entsteht.

**Die Liste.** Eine Seite holt `limit` (Standard 20, max 50) Profile per
Cursor aus der Datenbank, fragt für diese parallel den Ledger und filtert. Sind
davon 7 freigegeben, kommen 7 zurück — nicht 20. Die Alternative wäre eine
Nachladeschleife; sie kostet unbestimmt viele Runden und verriete über deren
Anzahl indirekt, wie viele Profile *nicht* freigegeben sind. `next_cursor` sagt,
ob es weitergeht; die Anzahl sagt nichts über die Gesamtmenge.

## 6. Fehlerverhalten

**Ohne Einwilligung antwortet der Fremdabruf `404`, nicht `403`.** Ein `403`
hieße „diese Person existiert, zeigt sich dir aber nicht" — und gäbe damit
genau preis, was sie zurückhalten wollte. `product-scope.md` verspricht
Kontrolle darüber, *ob* jemand auffindbar ist; „versteckt" muss von „gibt es
nicht" ununterscheidbar sein. Dasselbe `404` gilt für eine unbekannte `subject_id`.

**Ohne aktiven Tenant antwortet der Fremdabruf `403`.** Das ist eine Aussage
über den Aufrufer, nicht über das Ziel, und verrät nichts.

**Ist consent-service nicht erreichbar, wird `503` geantwortet — fail closed.**
Weder `404` (eine Lüge: wir wissen es nicht) noch die Anzeige des Profils.
Bei der Liste scheitert die ganze Seite statt still eine leere zu liefern; eine
leere Liste läse sich als „niemand hat freigegeben", was ebenfalls unwahr wäre.

`422` bei Verstoß gegen die Feldgrenzen, `401` ohne Token.

## 7. Tests

**Unit (ohne Docker):** Feldgrenzen und Normalisierung der Skills; Aggregat legt
`updated_at` fort; Handler für das eigene Profil; Handler für den Fremdabruf
gegen einen `FakeConsentGate` — freigegeben, nicht freigegeben, Ledger nicht
erreichbar; Listen-Filter inklusive „Seite liefert weniger als `limit`".

**Integration (Testcontainers, self-skip ohne Docker):** Migration hoch und
runter; Repository-Roundtrip inklusive JSONB-Skills; die Endpunkte über HTTP
mit einem echten identity-Token. **Der tragende Test:** Profil anlegen → Consent
erteilen → Fremdabruf `200` → Consent widerrufen → Fremdabruf `404`, ohne
Wartezeit dazwischen. Das ist die Sofortwirkung aus ADR-0013, an der Stelle
belegt, an der sie zählt.

**Frontend (Vitest, test-first):** Bearbeitungsformular; der
Sichtbarkeitsschalter ruft consent-service, nicht profile-service; die
Kandidatenliste zeigt eine leere Menge ohne Fehlermeldung.

**E2E (Playwright, self-skip ohne laufenden Stack):** registrieren → bestätigen →
anmelden → Profil ausfüllen → sichtbar schalten → als Unternehmen anmelden →
Profil in der Liste finden → Kandidat widerruft → Profil verschwindet.

## 8. Validierungsskript

`scripts/validate.sh` — ein Befehl, der den ganzen Stand prüft und am Ende eine
Zeile pro Prüfung ausgibt: Formatierung, Lint, Typen, Python-Tests inklusive
Skip-Zahl (mehr als drei Skips heißt: Docker läuft nicht, die Integrationsschicht
ist ungeprüft), Frontend-Typen und -Tests, Compose-Konfiguration, und — falls der
Stack läuft — die E2E-Suite. Rückgabewert ungleich null, sobald etwas rot ist.

Das Skript ist die ausführbare Fassung dieser Spec: was hier steht, prüft es.

## 9. Definition of Done

- `worker new-service` erzeugt einen Service, der sich **importieren** lässt, und
  `apps/profile-service` ist daraus entstanden.
- Eine Person füllt ihr Profil im Browser aus und schaltet es sichtbar.
- Ein Unternehmen findet es in der Liste; ohne Freigabe ist es nicht auffindbar.
- Ein Widerruf wirkt beim nächsten Abruf, ohne Wartezeit.
- `./scripts/validate.sh` läuft grün, inklusive Playwright bei laufendem Stack.
- ADR-0020 geschrieben, ROADMAP und CLAUDE.md nachgezogen.
