# Stand der Migration

**Diese Datei wird fortgeschrieben, nicht neu geschrieben.** Wer die Arbeit
aufnimmt, liest sie zuerst und weiß dann, wo es weitergeht — ohne den Verlauf zu
durchsuchen oder sich den Zustand aus dem Repository zusammenzureimen.

Der Auftrag steht in [`MIGRATION-AUFTRAG.md`](MIGRATION-AUFTRAG.md), das
Nachschlagewerk in [`MIGRATION-PROMPT.md`](MIGRATION-PROMPT.md). Hier steht nur,
was davon getan ist.

**Zuletzt fortgeschrieben:** 2026-08-23, nach dem Abbruch von Welle 1.
**Zweig:** `dotnet-migration`. **Girder:** 3.0.1.

---

## Kurz

| | |
|---|---|
| **Phase A — Fundament** | **fertig**, committet |
| **Phase B — die neun Dienste** | **Welle 1 abgebrochen**, Arbeit gesichert in `02d80a4` |
| **Phase C — Zusammenbau** | nicht begonnen |
| **Prüfer** | nicht begonnen |
| **Tests** | 211 grün, 0 rot, 0 übersprungen |
| **Offene Girder-Schulden** | keine |
| **Nicht in der Solution** | consent, profile, resume — sie übersetzen noch nicht |

Prüfen lässt sich das mit zwei Aufrufen, **getrennt**:

```bash
dotnet build dotnet/WorkerTransfer.slnx
dotnet test  dotnet/WorkerTransfer.slnx --blame-hang-timeout 300s
```

---

## Phase A — fertig

Vier Stücke, alle committet und grün.

**1. `dotnet/src/shared/WorkerTransfer.Outbox`** — Tabelle je Dienst in dessen
eigener Datenbank (`ConfigureOutbox()`), Absicht in derselben Transaktion
(`IOutbox.VermerkeAsync`), `FOR UPDATE SKIP LOCKED` beim Holen,
`NochNichtException` für „noch nicht" ohne Versuchsverbrauch,
`HoechsteVersuche = null` heißt nie aufgeben, kein Inhalt außer `user_id` und
`kind`. Die Schleife gehört dem Dienst und öffnet je Durchlauf einen Bereich.

**2. `dotnet/src/shared/WorkerTransfer.ServiceDefaults`** —
`AddWorkerTransferDefaults(...)` und `UseWorkerTransferDefaults(...)`.
`AlsAussteller()` ruft **nur identity**. Die RFC-9457-Problemdetails liegen hier.

**3. Die drei Verträge** — `WorkerTransfer.Contracts.Identity` (Tokenform und
was niemals hineingehört), `.Consent` (Frage, Antwort, Sammelfrage,
`HoechsteSammelgroesse = 100`, `Zwischenspeicherdauer = TimeSpan.Zero`),
`.Erasure` (Löschabsicht ohne Begründungsfeld, Quittung, Unternehmensrückzug,
Empfängerliste).

**4. `identity-service` vollständig** — 18 Routen, die Vorlage für alle anderen:

| Bereich | Routen |
|---|---|
| Anmeldung | `POST /auth/{login,refresh,logout,company/{id}}`, `GET /auth/session`, `GET /me` |
| Registrierung | `POST /auth/{register,verify-email,resend-verification}` |
| Unternehmen | `POST /companies`, `GET /me/companies`, Einladungen anlegen/listen/zurücknehmen, Mitglieder listen/entfernen, `POST /invitations/accept` |
| Löschung | `POST /account/erasure` |

Die drei Benachrichtigungsrouten liegen **absichtlich nicht** hier, sondern
gehen an `notification-service`.

---

## Phase B — die neun Dienste

Ein erster Versuch mit neun gleichzeitigen Agenten ist am Ausgabelimit
gestorben, bevor einer eine Datei angelegt hatte. Der zweite Versuch — Welle 1
mit dreien — ist **ebenfalls am Ausgabelimit gestorben**, aber deutlich später:
alle drei hatten Domäne, Anwendung und Infrastruktur weitgehend geschrieben.

**Diese Arbeit ist in `02d80a4` gesichert.** Der Commit baut nicht und soll es
nicht; er verhindert nur, dass ein unachtsames Kommando sie kostet. Die
Projekte stehen **absichtlich nicht** in `WorkerTransfer.slnx`: solange sie
nicht übersetzen, würde das den Gesamtbau roten, und dann sagt `dotnet build`
über identity und die geteilten Pakete nichts mehr aus.

### Wo die drei genau stehen

| | consent | profile | resume |
|---|---|---|---|
| Domain | steht | steht | steht |
| Application | steht | steht | steht |
| Infrastructure | steht | steht | steht |
| Contracts | fehlt | leeres Projekt | steht |
| **Api** | **fehlt ganz** | nur `Program.cs`, **keine Endpunkte** | nur `.csproj`, **kein `Program.cs`** |
| **EF-Migrationen** | **fehlen** | **fehlen** | **fehlen** |
| Tests | 1 Datei | 4 Dateien | **keine** |
| Übersetzt | ungeprüft | ungeprüft | ungeprüft |

**Allen dreien fehlt dasselbe:** die Api-Schicht mit den Routen, die
EF-Migrationen, und der Nachweis, dass es übersetzt und grün ist. Keinem fehlt
nur eine Kleinigkeit.

### So wird Welle 1 fortgesetzt

Je Dienst ein Agent, mit demselben Auftrag wie beim Start — **plus dem Hinweis,
dass Domäne, Anwendung und Infrastruktur schon liegen und er dort weitermacht,
statt neu anzufangen.** Reihenfolge der verbleibenden Arbeit:

1. Api-Projekt anlegen bzw. vervollständigen: `Program.cs` auf
   `AddWorkerTransferDefaults` / `UseWorkerTransferDefaults`, dann die Routen.
2. EF-Migration erzeugen (`dotnet ef migrations add …`).
3. Tests schreiben — Einheitentests **und** Integrationstests gegen
   Testcontainers, mit `Database.MigrateAsync()`.
4. Gegenproben fahren.
5. Bauen und testen, **in getrennten Aufrufen**, nur auf den eigenen Projekten.
6. Committen; damit ist die WIP-Bedingung für diesen Dienst aufgehoben.

Erst wenn ein Dienst übersetzt und grün ist, kommt er in die Solution.

### Was nach jeder Welle zu tun ist

1. Die neuen Projekte in `dotnet/WorkerTransfer.slnx` eintragen — **die Agenten
   fassen die Datei nicht an**, damit sie sich nicht gegenseitig überschreiben.
2. Von den Agenten gemeldete Paketversionen in `dotnet/Directory.Packages.props`
   nachtragen — aus demselben Grund.
3. `dotnet build` und `dotnet test` über die ganze Solution, **in getrennten
   Aufrufen**.
4. Diese Datei fortschreiben: Stand je Dienst, Commit, offene Befunde.

### Offene Punkte, die ein Agent gemeldet hat

Keiner der drei kam bis zu seinem Bericht. Was beim Fortsetzen zu klären ist:

- **Die Fähigkeitentabelle gehört geteilt.** `profile` hat sie dienstintern
  gebaut (`Domain/Faehigkeiten/Wortschatz.cs`); `jobs` braucht dieselbe in
  Welle 2. Spätestens dann nach `dotnet/src/shared/WorkerTransfer.Skills`
  ziehen — nicht vorher, sonst schreiben zwei Agenten dieselbe Datei.
- **Ob die drei überhaupt übersetzen, ist ungeprüft.** Sie standen nie in der
  Solution und wurden nie gebaut.

### Eine Lehre, die bleibt

Zwei Anläufe sind am Ausgabelimit gescheitert, nicht an der Sache. Drei Agenten
gleichzeitig reichen aus, um es zu erschöpfen, wenn jeder einen Dienst von der
Domäne bis zur Api baut. Beim nächsten Anlauf bekommt **ein Agent einen Dienst
und einen abgeschlossenen Abschnitt** — erst Api und Migration, dann in einem
zweiten Lauf die Tests —, statt alles in einem Zug.

---

## Phase C — Zusammenbau

Nicht begonnen. In dieser Reihenfolge:

1. Gateway mit Ocelot, eine Route je Dienst, `Sec-Fetch-Dest: document` trennt
   Seite von Ressource.
2. Compose und Helm auf die .NET-Dienste.
3. Python restlos entfernen, danach `dotnet/` flach in die Wurzel — als eigener
   Commit, damit die Umbenennungen lesbar bleiben.
4. Übergangsgerüst löschen: Ü-1 bis Ü-7 in
   [`uebergang-python-dotnet.md`](uebergang-python-dotnet.md), ersatzlos. Die
   Enum-Guards werden schlichte `CREATE TYPE`.
5. CI neu — und sie baut diesmal die Images.
6. Der Prüfer, gegen die Vision statt gegen Python, mit `CLAUDE.md`-Neufassung.

---

## Was beim Weiterarbeiten immer gilt

- **Bauen und Testen in getrennten Aufrufen.** Verkettet scheitern die
  Testcontainers-Reihen und sehen dabei aus wie echte Testfehler.
- **Docker muss laufen**, sonst fallen die Integrationstests aus — und ein
  ausgefallener Integrationstest sieht aus wie ein bestandener.
- **Kein Umweg um einen Girder-Fehler.** Code richtig schreiben, Test rot
  lassen, Ticket nach `bugs/` mit Reproduktion ohne WorkerTransfer-Code,
  weitermachen.
- **Gegenproben fahren**, und darauf achten, dass der Bruch **übersetzt**: ein
  Build-Fehler sieht in der Ausgabe aus wie ein bestandener Test.
