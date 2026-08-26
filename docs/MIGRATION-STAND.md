# Stand der Migration

**Diese Datei wird fortgeschrieben, nicht neu geschrieben.** Wer die Arbeit
aufnimmt, liest sie zuerst und weiß dann, wo es weitergeht — ohne den Verlauf zu
durchsuchen oder sich den Zustand aus dem Repository zusammenzureimen.

Der Auftrag steht in [`MIGRATION-AUFTRAG.md`](MIGRATION-AUFTRAG.md), das
Nachschlagewerk in [`MIGRATION-PROMPT.md`](MIGRATION-PROMPT.md). Hier steht nur,
was davon getan ist.

**Zuletzt fortgeschrieben:** 2026-08-26, nach `applications`.
**Zweig:** `dotnet-migration`. **Girder:** 3.0.1.

---

## Kurz

| | |
|---|---|
| **Phase A — Fundament** | **fertig**, committet |
| **Phase B — die neun Dienste** | **6 von 9 fertig.** Welle 2 ist durch; offen ist Welle 3: `companies`, `transfer`, `notification` |
| **Phase C — Zusammenbau** | nicht begonnen |
| **Prüfer** | nicht begonnen |
| **Tests** | 398 grün, 0 rot, 0 übersprungen |
| **Offene Girder-Schulden** | keine |

Prüfen lässt sich das mit zwei Aufrufen, **getrennt**:

```bash
dotnet build dotnet/WorkerTransfer.slnx
dotnet test  dotnet/WorkerTransfer.slnx --blame-hang-timeout 300s
```

**Der zweite Aufruf reicht auf dieser Maschine nicht mehr.** Neun Testreihen
gleichzeitig starten neun Container, und der `ResourceReaper` von Testcontainers
läuft dabei in eine Zeitüberschreitung — *alle* Reihen fallen dann binnen einer
Millisekunde mit `TypeInitializationException`, was wie ein kaputter Bau
aussieht und keiner ist. Die Reihen einzeln fahren:

```bash
cd dotnet
for p in Outbox Skills Identity Consent Profile Resume Portfolio Jobs Applications; do
  dotnet test tests/WorkerTransfer.$p.Tests/WorkerTransfer.$p.Tests.csproj --no-build \
    | grep -E "^(Bestanden!|Fehler!)"
done
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

### Wie Welle 1 zu Ende ging

Die Agenten hatten Domäne, Anwendung und Infrastruktur weitgehend geschrieben,
als sie am Ausgabelimit abbrachen. Die fehlende Hälfte — Api-Schicht, Routen,
EF-Migration, Integrationstests, Gegenproben — ist von Hand nachgezogen worden,
ein Dienst nach dem anderen.

**Das Muster ist wiederholbar** und steht so für Welle 2 und 3 bereit:

1. Fehlendes in Infrastructure ergänzen (DI-Registrierung, Zustellungen,
   Sicherheitsadapter).
2. Api-Projekt: `Program.cs` auf `AddWorkerTransferDefaults` /
   `UseWorkerTransferDefaults`, dann die Routen.
3. `dotnet ef migrations add …`.
4. Integrationstests gegen Testcontainers mit `Database.MigrateAsync()`.
5. Gegenproben — jede tragende Regel brechen, prüfen dass genau die zugehörigen
   Tests fallen, und dass der Bruch **übersetzt**.
6. In `WorkerTransfer.slnx` eintragen, Gesamtlauf, committen.

**Vier Testerwartungen waren falsch und nicht der Code** — das ist der
wertvollste Befund aus Welle 1. Die von den Agenten geschriebene Domäne war
jedes Mal besser durchdacht als die Erwartung an sie. Wer Welle 2 fortsetzt,
sollte bei einem roten Test zuerst fragen, ob der Test recht hat.

### Offene Punkte, die ein Agent gemeldet hat

Keiner der drei kam bis zu seinem Bericht. Was beim Fortsetzen zu klären ist:

- **Die Fähigkeitentabelle gehört geteilt.** `profile` hat sie dienstintern
  gebaut (`Domain/Faehigkeiten/Wortschatz.cs`); `jobs` braucht dieselbe in
  Welle 2. Spätestens dann nach `dotnet/src/shared/WorkerTransfer.Skills`
  ziehen — nicht vorher, sonst schreiben zwei Agenten dieselbe Datei.
- **Erledigt:** die Fähigkeitentabelle liegt jetzt in
  `dotnet/src/shared/WorkerTransfer.Skills`, ohne eine einzige Abhängigkeit.
  Geteilt ist nur die Tabelle und die Höchstlänge; die Fähigkeitenliste selbst
  führt jeder Dienst für sich.
- **`ZugriffsCookie` ist nach `ServiceDefaults` gewandert.** Es stand in drei
  Diensten; die dritte Kopie ist die, ab der eine Regel je Dienst zu driften
  beginnt.

### Eine Lehre, die bleibt

Zwei Anläufe sind am Ausgabelimit gescheitert, nicht an der Sache. Drei Agenten
gleichzeitig reichen aus, um es zu erschöpfen, wenn jeder einen Dienst von der
Domäne bis zur Api baut. Beim nächsten Anlauf bekommt **ein Agent einen Dienst
und einen abgeschlossenen Abschnitt** — erst Api und Migration, dann in einem
zweiten Lauf die Tests —, statt alles in einem Zug.

---

## Welle 2 ist durch — `portfolio`, `jobs`, `applications`

Alle drei nach dem Muster oben von Hand gebaut, jeder mit eigener EF-Wanderung,
Testcontainers-Reihe und Gegenproben.

### `applications-service` (Port 8007)

Sechs Routen plus die Löschung: `POST /applications`, `GET /applications/me`,
`POST /applications/{id}/withdraw`, `POST /applications/{id}/status`,
`GET /jobs/{id}/applications`, `GET /companies/me/application-stats`,
`POST /erasure`.

Vier Entscheidungen tragen und lassen sich durch „Aufräumen" leicht umkehren:

- **Der Aufbewahrungsschalter steht auf aus und ist keine Einstellung.**
  `Aufbewahrung.EingestellteBehalten` ist `false` und deckt **genau eine
  Zeilenklasse** ab: `status = 'hired'`. Er reist als *Parameter* in
  `ILoeschbestand.LoescheAsync`, statt dort gelesen zu werden — sonst ließe sich
  nur prüfen, dass er aus steht, nicht was der umgelegte Schalter abdeckt, und
  genau das ist die Aussage, auf die es ankommt.
  Er ist **`static readonly` und nicht `const`**, aus gemessenem Grund: ein
  `const` wird in jede lesende Assembly hineinkopiert, und bei einer Gegenprobe
  fiel ein Test über unverändertem Code, weil die Testassembly noch den alten
  Wert trug.
- **Erst der Ledger, dann der Vorgang, dann der Commit.** Schweigt der Ledger,
  fliegt `EinwilligungSchweigt` durch die Transaktion und es entsteht keine
  Bewerbung. Der Rückzug widerruft dafür **bedingungslos alle drei** Freigaben,
  auch die nie erteilten.
- **Die Outbox statt fire-and-forget.** Nur der Zug des Unternehmens wird
  vermerkt (`application_update`), in derselben Transaktion; der Rückzug durch
  die Person nicht — sie weiß, was sie getan hat.
- **Zahlen nur über die eigenen Vorgänge.** Kein Consent-Aufruf, weil gezählt
  wird, was das Unternehmen ohnehin einzeln sieht. Die Grenze verläuft bei der
  *Zusammenführung*, nicht bei der Aggregation (ADR-0022/0026).

### Ein Fehler, der drei fertige Dienste betraf

Ein Endpunktfilter, der die Antwort selbst schreibt und danach **`null`**
zurückgibt, lässt das Rahmenwerk ein zweites Mal schreiben — JSON-`null` samt
Kopfzeilen, die zu diesem Zeitpunkt schon stehen. Bei einer Anfrage **ohne
Rumpf** sieht der Aufrufer trotzdem sein 503, und nur das Protokoll trägt eine
unbehandelte Ausnahme; bei einer **mit Rumpf** reißt die Verbindung, und er
bekommt `Error while copying content to a stream` statt eines Statuscodes.

Deshalb war es in `resume`, `profile` und `portfolio` unsichtbar: deren
503-Tests sind GET-Aufrufe. Aufgefallen ist es erst an `POST /applications`.
Alle vier Dienste geben jetzt `Results.Empty` zurück, mit dem Grund an der
Stelle. `Ein_schweigender_Ledger_laesst_keine_Bewerbung_zurueck` ist der Test,
der es festnagelt.

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
- **Nach einer Gegenprobe `--no-incremental` bauen.** Der inkrementelle Bau hat
  die zurückgenommene Änderung zweimal nicht bemerkt; der Test fiel dann über
  Code, der längst wieder richtig war — und der nächste Schluss daraus wäre
  falsch gewesen.
