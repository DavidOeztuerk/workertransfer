# Übergabe — Stand und wo weitergemacht wird

**Zweck.** Diese Datei ist der Wiedereinstieg. Wer hier weiterarbeitet, liest
sie zuerst und braucht sonst nichts aus dem Gedächtnis.

**Stand:** 02.09.2026 · Branch `dotnet-migration` · nichts gepusht (kein Upstream)

---

## Was fertig und committet ist

| Commit | Inhalt |
|---|---|
| `fdc6c79` | **H2** — vier Messungen an Girder 4.0.2 |
| `19f45b6` | **Draht-Fund** — snake_case durchgesetzt, `DrahtvertragTests` |
| `f915b14` | **Frontend-Fundament** — Tokens, Theme, Store, Header |
| `81d74d8` | **Einstiegspunkt** — Provider, ThemeProvider, `createBrowserRouter` |

**Grün zuletzt gemessen:** 691 .NET-Tests · Routenkarte 318 · Frontend 464 Unit
+ Build · E2E 12 von 20.

---

## Vier Agenten wurden gestartet — alle vier sind gefallen

Drei, weil der Rechner in den Ruhezustand ging
(`API Error: Your computer went to sleep mid-response`), einer am Sitzungslimit.
**Der Arbeitsbaum blieb sauber**: keine kaputten Dateien, keine laufenden
Behälter, `npx tsc --noEmit` geht durch.

| Strom | Territorium | Docker | hinterlassen |
|---|---|---|---|
| Korrelation & Protokolle | nur lesend, misst am Stapel | **exklusiv** | nichts — starb vor dem Schreiben |
| Frontend `public`+`auth` | `apps/web/src/features/{public,auth}/` | nein | Zwischenstand ✓ · `public/test/render.tsx` + `public/pages/HomePage.tsx` |
| Frontend `person` | `apps/web/src/features/person/` | nein | nichts — starb vor dem Schreiben |
| Frontend `work`+`company` | `apps/web/src/features/{work,company}/` | nein | Zwischenstand ✓ |

Die zwei überlebenden Zwischenstände (`docs/uebergabe/frontend-*.md`) enthalten
die **geplante Reihenfolge der Seiten** und die **Lesebefunde aus dem alten
Code**. Wer neu startet, liest sie zuerst — sie ersparen die ganze Lesephase.

Neu zu stellen sind vier Aufträge; zwei davon (`public`+`auth`,
`work`+`company`) können am hinterlassenen Zwischenstand ansetzen.

**Die Territorien überschneiden sich nicht, und das ist die Bedingung.** Nur ein
Strom darf Docker: Testcontainers und Compose konkurrieren, und das Ergebnis
sieht wie ein echter Testfehler aus.

**Die Lehre, die beim Neustart etwas ändert:** Agenten müssen den Auftrag zum
Zwischenstand **beim Start** bekommen, nicht erst, wenn es knapp wird — mit der
Auflage, ihn nach jedem grösseren Schritt fortzuschreiben. Von vier Agenten
haben nur die zwei etwas hinterlassen, die die Anweisung noch rechtzeitig
erreicht hat, und die beiden anderen haben ihre gesamte Lesephase mitgenommen.

---

## Was NOCH NICHT angefangen ist

### 1. Die Umbenennung ins Englische — **die grösste offene Arbeit**

Entschieden vom Auftraggeber: **„Alles ausser der UI".** Also Datei- und
Ordnernamen, Klassen, Methoden, Variablen, Testnamen **und die deutschen
XML-Doc-Kommentare** → Englisch. **Die Oberfläche bleibt deutsch.**

Umfang, gemessen: **327 von 751 `.cs`-Dateien** tragen deutsche Namen, dazu
Dutzende Ordner (`Nachrichten/`, `Einwilligung/`, `Bewerbungen/`, `Stellen/`,
`Markt/`, `Post/`, `Pruefspur/` …) und tausende Bezeichner.

**Vorgehen, das gelten soll:** wellenweise **je Dienst**, nicht quer. Nach jeder
Welle `dotnet build` und die betroffene Reihe **einzeln** — nie `dotnet test`
über die Projektmappe. Erst wenn eine Welle grün ist, die nächste.

Namen, die durchgehend fallen (Vorschlag, beim ersten Dienst festzurren):

```
Bremse            -> RateLimiter          Dienstgrundlage -> ServiceDefaults
IBefehl / IAbfrage-> ICommand / IQuery    Umgebung        -> Environment
Wanderung         -> Migration            Personenzeile   -> PersonRow
Aufbewahrung      -> Retention            Pruefspur       -> AuditTrail
Einwilligung      -> Consent              Loeschung       -> Erasure
Nachrichten/      -> Messages/            Stellen/        -> Jobs/
Bewerbungen/      -> Applications/        Markt/          -> Market/
Drahtvertrag      -> WireContract         Zwischenspeicher-> Cache
```

**Achtung, sonst geht etwas kaputt:**
- `Personenzeile` ist eine **EF-Annotation**, die der Löschwächter liest —
  Umbenennen heisst: beide Seiten in einem Schritt.
- Migrationsdateien unter `Persistence/Migrations` tragen Klassennamen, die in
  der Datenbank als `__EFMigrationsHistory` stehen. **Nicht umbenennen.**
- Die deutschen Testnamen sind Dokumentation (`Die_Abweisung_nennt_kein_Konto`).
  Beim Übersetzen den Satz erhalten, nicht auf ein Verb kürzen.
- `docs/routenkarte.yml` und `scripts/routenkarte.sh` nennen Pfade, keine
  Bezeichner — die bleiben.

### 2. Die restlichen Prüferfunde (H5)

Aus dem Bericht des fremden Prüfers, nach Schwere:

| # | Fund | Ort |
|---|---|---|
| 2 | **Token trägt drei Ansprüche mehr als zugesagt**; `email_verified` ist IMMER `false`, `account_status` IMMER `"Active"` — auch für ein gesperrtes Konto | Girder `JwtService.cs:111-118`, `UserClaims.cs:14-15` |
| 7 | `ci.yml` definiert `images` und `dependency-audit` **doppelt** (Zeilen 94/349, 265/516) | `.github/workflows/ci.yml` |
| 5 | Gateway-eigene Antworten (Gesundheit, **jede 429**) tragen keine Sicherheitsköpfe | `src/gateway/.../Program.cs` |
| 3 | Kommentar behauptet einen Test, den es nicht gibt | `Pruefspur/Pruefhandlung.cs:19` |
| 4 | `HasPostgresEnum<T>("pruef_handlung")` nimmt das Argument als **Schema**, nicht als Typnamen → Waisen-Schema | `ProfileDbContext.cs:112` |
| 6 | `Communication` und `Encryption` haben nirgends im Code eine Zeile; **kein Test prüft die Zusammensetzung** | `Dienstgrundlage.cs` |
| 8 | `CLAUDE.md` beschreibt noch Girder 3.0.1 — an zehn Stellen falsch | `CLAUDE.md` |

**Fund 2 ist der gefährlichste:** wer die zugehörigen Girder-Richtlinien
einschaltet, sperrt entweder alle aus oder lässt Gesperrte durch. Er gehört in
ein Girder-Ticket, nicht in einen Umweg hier.

### 3. Aus dem KI-Prüfbericht (`docs/KI-EINSATZ-PRUEFUNG.md`)

- **Drei weitere ADRs behaupten Tests, die es nicht gibt** (ADR-0024 §3 und §4,
  ADR-0026). `HttpEntwerfer` kommt in `tests/` überhaupt nicht vor.
- `ANTHROPIC_API_KEY` ist in `docker-compose.yml` **an keinen Dienst gereicht** —
  nur Helm verdrahtet ihn. Lokal gesetzt bewirkt er nichts.
- `wish` ist unbegrenzt; bei `/jobs/draft` ist der ganze Prompt aufrufergesteuert.
- Ein Zeitüberlauf wirft `TaskCanceledException` statt `HttpRequestException` →
  **500 statt der versprochenen 503**, zweimal im Baum.

### 4. Frontend: was nach den Agenten noch fehlt

- **Altes entfernen:** `src/app.tsx`, `src/routes/`, die alten Feature-Ordner
  (`src/auth/`, `src/profile/`, `src/jobs/` …), `packages/ui` und `styles.css`.
  Erst wenn ALLE drei Agenten gelandet sind — sie lesen daraus als Vorlage.
- **E2E nachziehen:** 8 der 20 Reisen sind an der Oberfläche vorbeigealtert
  (Formular liegt auf `/company/jobs/new`, die Reise sucht es auf
  `/company/jobs`; gleiches Muster bei Team-Einladung und Bewerbung).
- **Playwright in `make check` und in CI aufnehmen.** Das ist die Lücke, hinter
  der sich der ganze Draht-Fund versteckt hat — sie ist wichtiger als die acht
  Reisen selbst.

---

## Regeln, die beim Weiterarbeiten gelten

- **Bauen und Testen in getrennten Aufrufen.** Nie `dotnet test` über die
  Projektmappe — fünfzehn Postgres-Behälter auf einmal, alle Reihen fallen
  binnen Millisekunden, und es sieht aus wie ein kaputter Bau. `scripts/test-dotnet.sh`.
- **Nie parallel bauen und testen**, und nie Compose neben Testcontainers.
- **Gegenprobe nach jedem Wächter**, und danach `--no-incremental` bauen —
  auch nach dem ZURÜCKNEHMEN, nicht nur nach dem Patch.
- **Kein Umweg um einen Girder-Fehler.** Code richtig schreiben, Test rot
  lassen, Ticket in `bugs/` mit Reproduktion ohne WorkerTransfer-Code, melden.
- **Messen statt vermuten.** Quelltext lesen ist kein Messen.
- **Ein Kommentar, der einen Test behauptet, ist kein Test.** In diesem Projekt
  wurden inzwischen **sechs** solche Behauptungen gefunden. Immer nachprüfen.
