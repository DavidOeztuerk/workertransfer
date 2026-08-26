# Stand der Migration

**Diese Datei wird fortgeschrieben, nicht neu geschrieben.** Wer die Arbeit
aufnimmt, liest sie zuerst und weiß dann, wo es weitergeht — ohne den Verlauf zu
durchsuchen oder sich den Zustand aus dem Repository zusammenzureimen.

Der Auftrag steht in [`MIGRATION-AUFTRAG.md`](MIGRATION-AUFTRAG.md), das
Nachschlagewerk in [`MIGRATION-PROMPT.md`](MIGRATION-PROMPT.md). Hier steht nur,
was davon getan ist.

**Zuletzt fortgeschrieben:** 2026-08-27, nach `notification` — Phase B ist durch.
**Zweig:** `dotnet-migration`. **Girder:** 3.0.1.

---

## Kurz

| | |
|---|---|
| **Phase A — Fundament** | **fertig**, committet |
| **Phase B — die neun Dienste** | **fertig**, 9 von 9 |
| **Phase C — Zusammenbau** | nicht begonnen |
| **Prüfer** | nicht begonnen |
| **Tests** | 495 grün, 0 rot, 0 übersprungen |
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
for p in Outbox Skills Identity Consent Profile Resume Portfolio Jobs Applications \
         Companies Transfer Notification; do
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

## Welle 3 — `companies`, `transfer` und `notification` stehen

### `companies-service` (Port 8008)

Vier Routen, eine Tabelle, **keine Löschung**: `PUT /companies/me/profile`,
`GET /companies/me/profile`, `GET /companies/by-slug/{kuerzel}`,
`GET /companies/{tenantId}/profile`.

Der kürzeste Kompositionswurzel-Aufruf im System, und das ist die Aussage: kein
Consent-Tor, keine Outbox, keine Zustellung, kein Löschbestand. Ein
Arbeitgeberprofil ist eine Aussage des Unternehmens über sich selbst — es gibt
niemanden, der einwilligen könnte, niemanden zu benachrichtigen und nichts über
einen natürlichen Menschen zu löschen. Der Dienst steht deshalb **nicht** in
`Loeschempfaenger.Fremde`; ein `POST /erasure` hier wäre ein Endpunkt, der
„erledigt" sagt, ohne je etwas getan zu haben.

Drei Entscheidungen tragen:

- **Das Kürzel ist ein Versprechen.** Abgeleitet aus dem Anzeigenamen, einmal
  beim ersten Speichern vergeben, danach unveränderlich — es gibt keinen Setzer
  dafür, und ein Test prüft genau das. Ein Kürzel, das dem Namen folgt, bricht
  jeden geteilten Link auf die Karriere-Seite. Die Mandanten-Kennung hängt nicht
  daran: sie stünde sonst in einer Adresse, die weitergegeben wird.
- **Drei der vier Routen brauchen keine Anmeldung.** Das ist ihr Zweck, nicht
  eine Lücke.
- **Erst entdoppeln, dann zählen.** Sonst würde jemand mit einundzwanzigmal
  „Homeoffice" abgewiesen, obwohl daraus ein Eintrag wird.

### Zwei Gegenproben, die nichts umgeworfen haben — und was daraus folgte

Beide Male war die *Prüfung* zu schwach, nicht der Code:

1. **Die Schemaprüfung des Links war ungedeckt.** Ohne sie blieb die Reihe grün:
   `javascript:alert(1)` hat gar keinen Wirt und fällt schon an der
   Wirtsprüfung. Der Test trägt jetzt zusätzlich `ftp://muster.example`.
2. **„Erst vollständig prüfen, dann schreiben" ließ sich über HTTP nicht
   widerlegen.** Der Speicher gibt gelesene Zeilen als *neues* Aggregat zurück
   und schreibt nur über `SichereAsync`, das nach einer Ausnahme nie erreicht
   wird — die halb geänderte Instanz wird verworfen. Die Regel schützt den, der
   das Aggregat über den Fehlschlag hinaus in der Hand behält, und wird jetzt
   dort geprüft: `ProfilregelnTests` fasst die Regeln am Aggregat statt an einer
   Route.

Und ein **falsch mitkopierter Satz**: der Python-Dienst muss `by-slug` vor die
`{tenant_id}`-Route ziehen, weil FastAPI nach Deklarationsreihenfolge trifft. In
ASP.NET trägt `{tenantId:guid}` eine Einschränkung, an der `by-slug` nie
vorbeikommt — getauscht gemessen, die Reihe blieb grün. Die Begründung steht
jetzt richtig an der Route.

### `transfer-service` (Port 8009)

Der größte Dienst der Migration: achtzehn Routen über drei Aggregate
(Marktstatus, Anfrage, Vorgang), plus die Löschung.

Der Marktstatus ist **die gefährlichste Angabe im ganzen System**. Ein
Lebenslauf verrät, wo jemand war; der Marktstatus verrät, dass er weg will — und
schon die *Existenz* der Aussage kann jemanden den Arbeitsplatz kosten. Alles
Folgende hängt daran:

- **Es gibt kein `:public`.** Die Freigabe nennt immer einen Empfänger. Beim
  Profil ist „für alle Unternehmen" eine sinnvolle Wahl; hier wäre sie ein
  Schalter, dessen Folgen niemand überblickt — darunter der eigene Arbeitgeber,
  der auf derselben Plattform ist.
- **Die Freigabe erlaubt zu sehen, nicht zu stören.** `unavailable` heißt nein,
  auch mit Freigabe. Kein Status, keine Freigabe und „gerade nicht" antworten
  buchstabengleich, sonst wäre der Endpunkt ein Orakel darüber, wer zuhört.
- **Die Anfrage setzt die *Profil*freigabe voraus, nicht die Existenz eines
  Marktstatus.** Beides zu prüfen wäre ein Orakel: „hat schon einen Marktstatus
  gepflegt" ist eine Information über die Person.
- **Der Widerruf wirkt im Ledger, nicht im Vorgang.** `GRANTED` bleibt stehen,
  `active` fällt auf falsch. Ein laufender Transfer bleibt bestehen: er hat
  seine eigene Tür und seine eigene Absage.
- **Der Aufbewahrungsschalter** `Aufbewahrung.BezahlteBehalten` steht auf
  `false` und deckt genau eine Zeilenklasse ab: ein abgeschlossener Handel
  *mit* Vergütung. Nicht `offered` — ein Gespräch ist kein Vertrag — und nicht
  `declined`/`withdrawn`.

Und die Regel, die der ULTRAPLAN so nicht zulässt: „beschäftigt → Firma muss
mitwirken" lässt sich nicht bauen, weil **die Plattform nicht weiß, wo jemand
arbeitet**. Ein Datensatz dafür wäre die Verbindung zwischen „arbeitet bei X"
und „hört zu" in einer einzigen Tabelle. Stattdessen trägt der Vorgang, dass
eine Freigabe *nötig* ist, und die Person bestätigt selbst, dass sie vorliegt —
schwächer und sicherer.

### Vier Gegenproben, die etwas über die Prüfungen gesagt haben

1. **Ein Widerruf, der 500 antwortet, wäre durchgegangen.** Der Test las danach
   den Stand und fand ihn unverändert — die Zurückrollung ließ ihn richtig
   aussehen. Jetzt wird der Statuscode mitgeprüft.
2. **„Die Freigabepflicht ist eingefroren" ließ sich über HTTP nicht
   widerlegen**, weil die Eigenschaft keinen Setzer hat. Wie beim Kürzel in
   companies wird jetzt genau das geprüft: *kann* nicht, nicht „wird nicht".
3. **Zwei Regeln sind strukturell, nicht geprüft — und das ist besser.**
   `Anfragebefehle` hat keinen `IMarktspeicher`, kann also gar nicht nach der
   Existenz eines Marktstatus fragen; der Widerruf-Pfad kann den Vorgang nicht
   umschreiben, weil das Aggregat nur aus `PENDING` heraus antwortet.
4. **Die Menge der laufenden Stände lässt sich nicht ändern.** Sie steht an
   *einer* Stelle (`Transferstaende.Laufende`) und speist sowohl das Aggregat
   als auch den Teilindex `uq_running_transfer`. Wird sie erweitert, ändert sich
   das EF-Modell, und der Dienst startet gar nicht mehr, bis eine Wanderung
   folgt — die Gegenprobe machte die *ganze* Reihe rot, in 23 ms.

### `notification-service` (Port 8010) — der einzige neue Dienst

Fünf Routen plus die Löschung: `POST /notifications` (der Diensteingang),
`GET /notifications/me`, `POST /notifications/me/read`,
`GET|PUT /me/notification-preferences`, `POST /erasure`.

**Der Einwand des Python-Dienstes stimmte, und er wird nicht ignoriert.** Dort
lagen die Benachrichtigungen bewusst in identity-service, mit der Begründung:
*„ein `notifications-service` bräuchte die E-Mail-Adresse; sie dorthin zu
kopieren oder über einen Lookup `subject_id → E-Mail` herauszureichen hieße, das
empfindlichste Datum des Systems zu vervielfachen — für eine Textmail."*

Beantwortet wird das, indem **die andere Hälfte umzieht**:

| | wo | was |
|---|---|---|
| **ob** etwas hinausgeht | notification-service | vier Schalter, Drossel, Postfach |
| **an wen** | identity-service | die Adresse, die dort ohnehin liegt |

Über die Grenze geht nur eine `userId` — die überall sonst auch schon geht.
Dazu kam **eine** neue Route in identity: `POST /internal/notify`, mit eigenem
Geheimnis (`Notify:Geheimnis`, ausdrücklich nicht das der Löschung), immer 202,
und ihr Rumpf trägt **weder Art noch Text**. Der Satz entsteht in identity,
für jede Art derselbe. Die Regel des Python-Dienstes — *der Aufrufer hat auf den
Text keinen Zugriff* — gilt damit über eine Dienstgrenze hinweg und ist stärker
geworden, nicht schwächer: notification-service **kann** nichts beisteuern.

Drei weitere Entscheidungen tragen:

- **Der Eintrag im Postfach entsteht immer, die Mail wird gedrosselt.** Das
  Postfach liegt hinter der Anmeldung, wo geprüft wird, wer liest; es zu
  drosseln hieße, jemandem zu verschweigen, dass etwas passiert ist. Die Mail
  landet in einem Postfach, das nicht nur der Person gehören muss.
- **Immer 202**, ob zugestellt oder nicht. Abbestellt, gedrosselt, Person
  unbekannt: der Aufrufer erfährt nichts — sonst wäre der Endpunkt ein Orakel
  über die Mitgliedschaft. Die einzige Ausnahme ist eine Art, die es nicht gibt:
  das ist ein Fehler des Aufrufers und keine Aussage über die Person.
- **Keine Outbox und keine Inhaltsspalte.** Er ist der Empfänger von Absichten,
  nicht ihr Absender; und was hier steht, landet in jeder Sicherung.

---

## Phase C — Zusammenbau

**Jetzt an der Reihe.** In dieser Reihenfolge:

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
