# Stand der Migration

**Diese Datei wird fortgeschrieben, nicht neu geschrieben.** Wer die Arbeit
aufnimmt, liest sie zuerst und weiß dann, wo es weitergeht — ohne den Verlauf zu
durchsuchen oder sich den Zustand aus dem Repository zusammenzureimen.

Der Auftrag steht in [`MIGRATION-AUFTRAG.md`](MIGRATION-AUFTRAG.md), das
Nachschlagewerk in [`MIGRATION-PROMPT.md`](MIGRATION-PROMPT.md). Hier steht nur,
was davon getan ist.

**Zuletzt fortgeschrieben:** 2026-08-27, nach dem **Übergangsgerüst** — Phase C, 4 von 6.
**Zweig:** `dotnet-migration`. **Girder:** 3.0.1.

---

## Kurz

| | |
|---|---|
| **Phase A — Fundament** | **fertig**, committet |
| **Phase B — die Dienste** | **fertig**, 10 von 10 |
| **Phase C — Zusammenbau** | **4 von 6**: Gateway, Compose, Helm, Python raus, Gerüst weg |
| **Prüfer** | nicht begonnen |
| **Tests** | 617 grün, 0 rot, 0 übersprungen |
| **Offene Girder-Schulden** | keine |

Prüfen lässt sich das mit zwei Aufrufen, **getrennt**:

```bash
dotnet build WorkerTransfer.slnx
dotnet test  WorkerTransfer.slnx --blame-hang-timeout 300s
```

**Der zweite Aufruf reicht auf dieser Maschine nicht mehr.** Neun Testreihen
gleichzeitig starten neun Container, und der `ResourceReaper` von Testcontainers
läuft dabei in eine Zeitüberschreitung — *alle* Reihen fallen dann binnen einer
Millisekunde mit `TypeInitializationException`, was wie ein kaputter Bau
aussieht und keiner ist. Die Reihen einzeln fahren:

```bash
make build   # dotnet build
make test    # scripts/test-dotnet.sh — die Reihen EINZELN
```

`scripts/test-dotnet.sh` ist der Grund, warum `make test` nicht `dotnet test`
über die Projektmappe ruft: fünfzehn Reihen starten dann fünfzehn Behälter
gleichzeitig, der `ResourceReaper` von Testcontainers läuft in eine
Zeitüberschreitung, und **alle** Reihen fallen binnen einer Millisekunde mit
`TypeInitializationException`. Das sieht aus wie ein kaputter Bau und ist
keiner. Das Skript läuft durch, nennt jede rote Reihe und am Schluss die Zahl.

---

## Phase A — fertig

Vier Stücke, alle committet und grün.

**1. `src/shared/WorkerTransfer.Outbox`** — Tabelle je Dienst in dessen
eigener Datenbank (`ConfigureOutbox()`), Absicht in derselben Transaktion
(`IOutbox.VermerkeAsync`), `FOR UPDATE SKIP LOCKED` beim Holen,
`NochNichtException` für „noch nicht" ohne Versuchsverbrauch,
`HoechsteVersuche = null` heißt nie aufgeben, kein Inhalt außer `user_id` und
`kind`. Die Schleife gehört dem Dienst und öffnet je Durchlauf einen Bereich.

**2. `src/shared/WorkerTransfer.ServiceDefaults`** —
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
  Welle 2. Spätestens dann nach `src/shared/WorkerTransfer.Skills`
  ziehen — nicht vorher, sonst schreiben zwei Agenten dieselbe Datei.
- **Erledigt:** die Fähigkeitentabelle liegt jetzt in
  `src/shared/WorkerTransfer.Skills`, ohne eine einzige Abhängigkeit.
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

## Nachtrag: `github-service` (Port 8011)

**Er war gestrichen, und die Streichung war ein Irrtum.** Verwechselt worden
waren das gelöschte *Paket* `worker-github` — das Menschen bewertete — und der
*Dienst*, der **unter** ADR-0022 gebaut wurde und ausdrücklich das Gegenteil
tut. Sein Domänenmodul sagt das im ersten Absatz.

Sechs Routen plus die Löschung: `POST /github/me` (Konto nennen),
`POST /github/me/{verify,refresh}`, `GET /github/me`, `DELETE /github/me`,
`GET /github/{subjectId}`, `POST /erasure`. Damit sind es **zehn Dienste**, und
die Löschkaskade hat wieder **acht** fremde Empfänger — `github` ist wieder
dabei, weil er eine Zeile je Mensch hält.

Die Grenze, an der genau hier gerutscht wird, steht jetzt nicht nur im Auftrag,
sondern als Test (`Adr0022Tests`):

| Erlaubt | Verboten |
|---|---|
| „Dieses Repository ist laut GitHub zu 80 % Go" | „Diese Person kann Go" |
| Die Person trägt Go **selbst** ins Profil ein | Der Dienst trägt es für sie ein |
| „Verbindung bewiesen" / „nicht bewiesen" | „zu 73 % vertrauenswürdig" |

Der Test greift Domäne und Vertrag ab und fällt, sobald irgendeine öffentliche
Fläche ein Wort wie `score`, `rank`, `level`, `bewert` oder `activity` trägt.
Dazu zwei Formprüfungen: ein `Repository` trägt genau sechs abgeschriebene
Felder, eine `Verbindung` keine Zusammenfassung.

Vier weitere Entscheidungen tragen:

- **Beim Nennen wird GitHub nicht gefragt.** Solange nichts bewiesen ist, gibt
  es nichts zu holen — und ein Abruf verriete nur, dass jemand nach diesem Konto
  gefragt hat.
- **Ein Kontowechsel setzt den Nachweis zurück** *und* würfelt die
  Einmalzeichenfolge neu. Sonst könnte jemand ein Konto nachweisen und danach
  den Namen auf ein fremdes drehen.
- **Sortiert wird nach Datum, nie nach Sternen.** Sterne messen Sichtbarkeit,
  nicht Arbeit, und eine Sortierung ist bereits eine Wertung.
- **Kein Hintergrundabgleich**, kein Nachtlauf, kein Webhook (ADR-0004): eine
  Plattform, die einem Menschen dauerhaft hinterhersieht, tut etwas anderes als
  eine, die einmal auf seine Bitte hinsieht.

### Drei Gegenproben, die über die Prüfungen etwas gesagt haben

1. **„Nur eine nachgewiesene Verbindung nimmt einen Abzug an" war ungedeckt.**
   Der Handler erreicht `Lege_ab` heute nur nach geglücktem Nachweis — die
   Prüfung ist eine zweite Verteidigungslinie, und genau die fällt lautlos,
   wenn jemand später einen Knopf „jetzt laden" baut. Jetzt am Aggregat geprüft
   (`VerbindungsregelnTests`).
2. **Der Fork-Filter war ungedeckt**, weil die Reisetests `IGitHub` ersetzen.
   Alles *im* Klienten war damit ungeprüft. Jetzt gibt es `HttpGitHubTests`
   gegen ein GitHub aus Papier — Forks, Feldabbildung, 404, Ratenlimit,
   Abfrageform.
3. **Ein Test von mir war falsch, nicht der Code:** `CanWrite` meldet auch
   einen *privaten* Setzer als schreibbar. Die Aussage, auf die es ankommt, ist
   „kein **öffentlicher** Setzer" — `GetSetMethod(nonPublic: false)`.

---

## Phase C — Zusammenbau

In dieser Reihenfolge:

1. ~~Gateway mit Ocelot~~ — **fertig**, siehe unten.
2. ~~Compose und Helm~~ — **fertig**. Compose gemessen, Chart gerendert.
3. ~~Python restlos entfernen, `dotnet/src → src`~~ — **fertig**.

   **Offen und bewusst verschoben:** die Pfadangaben in `docs/`, `AGENTS.md`,
   `CONTRIBUTING.md` und `README.md` zeigen noch auf `apps/<dienst>` und
   `packages/worker-*`. Sie gehören in Schritt 6, wo der Prüfer die
   Dokumentation ohnehin neu fasst — sie hier nachzuziehen hieße, dieselbe
   Datei zweimal anzufassen.
4. ~~Übergangsgerüst löschen~~ — **fertig**, siehe unten.
5. CI neu — und sie baut diesmal die Images.
6. Der Prüfer, gegen die Vision statt gegen Python, mit `CLAUDE.md`-Neufassung.

---

## Phase C, Schritt 1: das Gateway (`src/gateway`)

Ocelot 25, elf Ziele, 41 Routen, und **es prüft nichts**: kein Token wird
gelesen, keine Einwilligung geprüft, keine Rolle erzwungen. Das tut jeder
Dienst für sich, und ein Gateway, das es ebenfalls täte, wäre eine zweite
Wahrheit über dieselbe Frage.

Was hier **nicht** steht, ist ebenso wichtig: `/erasure` und
`/internal/notify` haben keine Route. Beide sind Dienst-zu-Dienst-Eingänge
hinter einem gemeinsamen Geheimnis; über den öffentlichen Ursprung erreichbar
wären sie „lösche alles über diesen Menschen", bewacht von einem Kopf.

### Drei Dinge, die gemessen wurden und nicht zu erraten waren

1. **Die `Sec-Fetch-Dest`-Regel passt nicht in die Ocelot-Landkarte.** Eine
   *wörtliche* Route gewinnt bei Ocelot **immer** gegen einen Platzhalter —
   `Priority` hin oder her. Eine Auffangregel `/{alles}` mit Kopfbedingung wird
   von `/jobs` also nie erreicht. Die Regel je kollidierendem Pfad zu
   wiederholen wäre möglich und wäre genau das, was sie vermeiden soll: zwei
   Pfadlisten, die auseinanderlaufen. Also setzt `Navigation.cs` **ein** Präfix
   (`/__ui`), und die Landkarte hat dafür **eine** Zeile. Der letzte
   Platzhalter ist gierig, deshalb trägt sie auch `/careers/muster` und
   `/assets/x/y.js`.
2. **`Priority` macht die Landkarte reihenfolgeunabhängig** — das ist ihr
   ganzer Zweck hier. Mit den ausgelieferten Werten ändert das Umdrehen aller
   Zeilen nichts; ohne sie entscheidet die Dateireihenfolge und acht Routen
   fallen um. Alle auf denselben Wert zu setzen sah zunächst harmlos aus: die
   Reihe blieb grün, weil die Zeilen zufällig richtig standen. Erst das
   Umdrehen hat es gezeigt — und `ReihenfolgeTests` hält es seither fest.
3. **`MapGet` läuft hinter Ocelot nie.** Ocelot beendet die Kette; die
   Gesundheitsproben mussten in eine Zwischenschicht **vor** ihr. Sie werden
   auch nicht weitergereicht: sonst kippte ein einzelner kranker Dienst das
   Gateway aus dem Lastverteiler und nähme die anderen zehn mit.

Zwei kleinere Befunde: Kommentare in `ocelot.json` sind erlaubt (der
JSON-Konfigurationsanbieter überliest sie) — deshalb trägt die Landkarte ihre
Begründung mit, wie die Traefik-Datei es tat. Und `X-Correlation-ID` wird hier
gesetzt, falls sie fehlt: ein Klick wird hinten zu drei Aufrufen, und ohne
gemeinsame Kennung erfindet jeder Dienst seine eigene.

**Die Ports:** identity 8001 … transfer 8009, notification **8010**, github
**8011**, Oberfläche 5173, Gateway 8090.

---

## Phase C, Schritt 2: Compose läuft, Helm rendert

`docker compose up` bringt Postgres, Mailpit, **elf Dienste**, das Gateway und
die Oberfläche hoch. Belegt, nicht behauptet: alle zwölf Häfen antworten mit
200, und eine echte Reise trägt durch das Gateway —
`register → Mail → verify → login → /me → /profiles/me → /market/me →
/notifications/me`, sieben Dienste in einer Kette über **einen** Ursprung.

**Ein Bild für alle elf** (`docker/dotnet-service.Dockerfile`): der
Einstiegspunkt liest `SERVICE_DIR` aus der Umgebung, `dotnet publish` legt jeden
Einstieg in sein eigenes Verzeichnis. Welche DLL zu starten ist, findet der
Einstiegspunkt über die einzige `*.runtimeconfig.json` im Verzeichnis — das
erspart eine Tabelle, die beim nächsten Dienst zu pflegen wäre.

**Der Bau braucht ein Geheimnis.** Girder liegt in GitHub Packages; die
NuGet-Konfiguration des Nutzers kommt als BuildKit-Geheimnis herein und wird nie
eine Schicht.

### Vier Dinge, die erst beim Laufen sichtbar wurden

1. **`identity-service` konnte sein Schema nicht anlegen.** Sein Grundschema —
   neun Tabellen, zwei Aufzählungen, `citext` — lag in acht Alembic-Wanderungen
   im Python-Baum; die .NET-Abbildung trug `ExcludeFromMigrations()` auf sechs
   Tabellen. Mit dem Umzug fällt die Ausnahme: eine EF-Wanderung `Grundschema`
   legt jetzt alles an, wie bei den zehn anderen. Die Testvorrichtung fährt
   nicht mehr `uv run alembic`, und `SpaltenetikettenTests` prüft die
   Etikettenmenge jetzt gegen die EF-Anmerkung statt gegen den Guard.
2. **Der Einstiegspunkt ließ jeden Dienst aus `/app` laufen** — damit las
   *keiner* seine eigene `appsettings.json`, und das Gateway starb an seiner
   fehlenden `ocelot.json`. Die Inhaltswurzel von ASP.NET ist das
   Arbeitsverzeichnis; der Einstiegspunkt wechselt jetzt hinein.
3. **`pg_isready` lügt.** Während initdb läuft, hört Postgres nur lokal, die
   Probe meldet trotzdem bereit, und die Dienste bekamen „connection refused".
   Die Probe fragt jetzt über TCP — *und* `Wanderung` wartet zusätzlich, weil es
   in Kubernetes gar kein `depends_on` gibt.
4. **Meine erste Wiederholung fing jede Ausnahme.** Damit wurde ein
   Schemafehler zu einer Minute Warten mit einer irreführenden letzten Meldung
   („Typ existiert bereits" — angelegt vom ersten Versuch, dessen Ursache
   niemand mehr sah). Sie wiederholt jetzt nur bei `DbException.IsTransient`
   oder einem `SocketException` in der Kette.

Dazu: Vite weist seit 6.x fremde Hosts ab, und Ocelot schickt den Host des
Ziels — `allowedHosts: ["web"]`, nur dieser eine Name.

### Helm: was wegfiel

Drei Dinge verschwinden mit Python, und jedes war eine Fehlerquelle:

- **Der `migrate`-initContainer.** Er fuhr `docker/entrypoint.sh true` und
  wartete davor mit einer Schleife auf Postgres. Jetzt wandert der Dienst beim
  Start selbst — `dotnet ef` braucht das SDK, und ein Laufzeitbild hat keins.
  Das Warten liegt damit ebenfalls im Dienst, was ohnehin richtig ist: in
  Kubernetes gibt es kein `depends_on`.
- **Der `uvicorn`-Aufruf im `command`.** Er umging den ENTRYPOINT, damit dieser
  nicht ein zweites Mal wanderte. Beides ist weg — und mit ihm die Frage, ob
  die zwei Beschreibungen desselben Starts noch übereinstimmen.
- **Traefik samt ConfigMap.** Das Gateway ist jetzt ein Dienst aus demselben
  Bild (`SERVICE_DIR=gateway`), und seine Landkarte reist *im Bild*. Damit
  fährt im Cluster, was Compose fährt; eine Kopie, die beim ersten neuen Pfad
  falsch wird, gibt es nicht mehr.

`replicaCount: 1` hat jetzt einen **vierten** Grund: zwei Pods sind zwei Pilger
auf demselben Schema.

### Der Wächter, der mit umgezogen ist

`tests/test_k8s_matches_compose.py` fiel — zu Recht, es prüfte gegen den
Python-Baum. Seine *Absicht* ist zu wertvoll zum Wegwerfen und lebt jetzt als
`TopologieTests` im Gateway-Projekt: Landkarte, Compose und Chart müssen
dieselbe Landschaft beschreiben. Vier Gegenproben, und eine davon hat eine
Lücke im Wächter selbst gezeigt — `CREATE DATABASE` traf auch in einer
auskommentierten Zeile, eine fehlende Datenbank blieb also grün.

**Noch offen (Schritt 3):** `make k8s-up` ist nicht gefahren. Das Chart lintet
und rendert, aber nur ein Lauf beweist, dass es läuft — und diese Maschine
verträgt fünfzehn Pods nicht neben einer Testreihe.

---

## Phase C, Schritt 4: das Übergangsgerüst ist weg

`docs/uebergang-python-dotnet.md` ist gelöscht. Was aus jedem Eintrag wurde:

| | wurde |
|---|---|
| **Ü-1** `verify_aud: False` | entfällt mit `worker_auth` |
| **Ü-2** nachsichtige Validatoren | **weg.** Aussteller und Zielgruppe werden wieder voll geprüft; `UebergangsTokenValidation` und `UebergangsPrincipalFactory` sind gelöscht, Girders eigene Fabrik liest wieder `tenant` |
| **Ü-3** `tenant_id` und `type` im Token | **weg.** Übrig ist Girders `tenant` |
| **Ü-4** der Python-Erneuerungstoken | war nie eine Schuld — eine einmalige Folge, die eingetreten ist |
| **Ü-5** `session_capacities` | **bleibt, und ist keine Schuld.** Die Begründung ist an `ISessionCapacity` gewandert, wo sie hingehört |
| **Ü-6** bcrypt schreibt | **bleibt vorerst** — jetzt als freie Entscheidung, nicht als Zwang |
| **Ü-7** die `CREATE TYPE`-Guards | **weg.** Kein `DO $$`-Block ist übrig |

Drei Dinge daran sind erwähnenswert:

- **Ü-7 hatte sich schon selbst erledigt.** Identitys Guard fiel mit der neuen
  Grundschema-Wanderung; consent-service hatte nie einen, weil sein .NET-Modell
  `varchar` mit Check-Constraint nimmt statt einer Postgres-Aufzählung — die
  Notiz beschrieb das *Alembic*-Schema.
- **Ü-5 wäre beim wörtlichen Löschen kaputtgegangen.** Der Eintrag sagt selbst:
  „solange nicht, bleibt die Tabelle". Ohne sie wäre, wer für eine Firma
  handelt, eine Viertelstunde später wieder Privatperson — lautlos, mitten in
  der Arbeit.
- **Ü-6 ist nicht erledigt, sondern entschieden-werden-dürfend.** bcrypt auf
  Argon2id umzustellen ist eine sicherheitsrelevante Änderung mit eigenem
  Commit, kein Nebenprodukt des Aufräumens. Der Grund steht jetzt im
  Kompositionswurzel-Aufruf.

`KreuzbeweisPythonNachDotnetTests` und `EchtePythonToken` sind gefallen: sie
belegten, dass .NET Token annimmt, die Python wirklich ausgestellt hat — und das
tut es jetzt bewusst nicht mehr.

**Zwei Tests hingen an Ü-3 und wurden dabei zuerst übersehen.**
`TokenformTests` nagelte fest, dass `tenant_id` und `type` im Token *stehen*;
nach dem Löschen fielen sie, und zwar zu Recht — der Lauf meldete 2 rot von 188.
Sie sind nicht gelöscht, sondern **umgedreht**: eine Theorie prüft jetzt, dass
beide Ansprüche **weg sind und wegbleiben**, in *beiden* Handlungsformen
(`tenant_id` entstand nur bei einer Firma, `type` immer — nur eine Form zu prüfen
ließe genau die andere Rückkehr durch). Gegenprobe gefahren: beide Ansprüche
wieder eingebaut, genau diese zwei Theoriefälle fielen, kein dritter. Danach
`--no-incremental` zurückgebaut, Reihe **189 grün, 0 übersprungen**.

Das ist der Regelfall bei so einem Löschen, nicht die Ausnahme: ein Test, der
eine Übergangsform *festhält*, wird beim Aufräumen zum Kandidaten — und die
richtige Antwort ist meistens, ihn ins Verbot zu drehen, nicht ihn wegzuwerfen.
Ein gelöschter Test hätte die Rückkehr von `tenant_id` niemand mehr gemeldet.

---

## Phase C, Schritt 5: eine CI, die Bilder baut

`.github/workflows/ci.yml` ist neu geschrieben. Vier Jobs statt drei:

| Job | fragt |
|---|---|
| `backend-quality` | `dotnet restore` → `build` → `./scripts/test-dotnet.sh` (bauen und testen in **getrennten** Schritten, Reihen einzeln) |
| `frontend-quality` | `pnpm check` · `test` · **`build`** |
| `images` | **baut beide ausgelieferten Bilder und fährt den ganzen Stapel hoch** |
| `dependency-audit` | `dotnet list package --vulnerable` und `pnpm audit --prod` |

Der dritte ist der neue. Bis hierher hat die CI nie einen Behälter gebaut —
genau so kam der Dependabot-Sprung auf node 25 durch und zerlegte das
Oberflächenbild. Er baut das Dienstbild (eins für alle zwölf, ADR-0028) und das
Oberflächenbild, fährt dann `docker compose up --wait` über alles außer `web`
und fragt zweimal durch das Gateway: lesend `/jobs`, schreibend
`POST /auth/register`. Erst das Schreiben belegt, dass die Wanderungen liefen —
der Lesepfad antwortet auch auf einer leeren Datenbank.

### Drei Dinge kamen beim Messen heraus, nicht beim Nachdenken

**1. Jeder Dienst meldete `unhealthy`, seit es die Compose-Datei gibt.**
Die Probe stand als `dotnet --version` in elf Blöcken — und das kann auf einem
**Laufzeitbild** nie gelingen: kein SDK, Abbruchcode 155, „No .NET SDKs were
found". Die Dienste antworteten die ganze Zeit tadellos (`/health/live` → 200),
aber `docker compose ps` zeigte eine Wand aus `unhealthy`. Eine Probe, die immer
rot ist, ist schlimmer als keine: nach der zweiten Woche liest sie niemand mehr.

Jetzt fragt **eine** Probe im Anker `x-dienst` — für alle elf **und** das
Gateway — nach `/health/live`, derselben Adresse, die auch Kubernetes abfragt.
Der Hafen kommt aus `ASPNETCORE_URLS` (`$${ASPNETCORE_URLS##*:}`), damit keine
zweite Liste von Häfen entsteht, die beim ersten neuen Dienst auseinandergeht.
Dafür liegt jetzt **curl im Laufzeitbild**: Docker fragt von *innen*, und das
Bild hatte weder curl noch wget noch nc, `sh` ist dash und kann kein
`/dev/tcp`. Kubernetes braucht das nicht — dort fragt das Kubelet selbst über
HTTP; die zwei Megabyte zahlt nur Compose.

**2. Das Gateway hatte gar keine Probe.** Es erbt sie jetzt aus demselben
Anker. Der Eingang war der einzige Behälter ohne Gesundheitsfrage.

**3. `GET /jobs` ist 401, nicht 200 — und `scripts/k8s-up.sh` behauptete 200.**
Eine Stellenliste steht hinter der Anmeldung, so ist der Dienst gebaut
(`StellenEndpoints`: `akteur.Current is null` → `NichtAngemeldet`). Das Skript
wäre bei seinem ersten Lauf an dieser Zeile gescheitert — es ist nie gelaufen
(siehe unten). Beide Stellen prüfen jetzt dasselbe: **401 und ein
RFC-9457-Dokument mit `correlationId`**. Der Beleg fürs Routen ist die Antwort,
nicht ihr Erfolg — so ein Dokument kann nur jobs-service geschrieben haben, eine
fehlende Route wäre ein leerer 404 vom Gateway und ein toter Dienst ein 502.

### Ein Entwurf, der verworfen wurde, weil die Gegenprobe ihn widerlegt hat

Der erste Entwurf des Bildjobs startete jeden der zwölf Dienste und fragte nach
drei Sekunden `docker inspect .State.Running`. Die Gegenprobe: dem Gateway seine
`ocelot.json` wegnehmen (`-v /dev/null:/app/gateway/ocelot.json`). Ergebnis —
der Dienst wirft eine unbehandelte Ausnahme direkt in `Program.Main`, **und der
Behälter läuft trotzdem weiter**, mit 99 % auf einem Kern, nach 45 Sekunden noch
`Running=true`. Die Prüfung war also grün über einem Dienst, der nichts als
einen Stacktrace produziert hat.

Deshalb steht dort jetzt `docker compose up --wait`: das wartet auf die
*Gesundheitsprobe* jedes Behälters statt auf seine bloße Anwesenheit.
Gegengeprobt: jobs-service eine unerreichbare Datenbank gegeben, `--wait` fällt
mit Abbruchcode 1 und „application not healthy after 45s".

### Was gemessen ist und was nicht

Lokal gefahren und grün: `make images` (beide Bilder), `docker compose up -d
--wait` über alle vierzehn Behälter (**alle healthy**, Abbruchcode 0),
`GET /jobs` → 401 mit `correlationId`, `POST /auth/register` → 201,
`docker compose config --quiet`, `make k8s-lint`, `pnpm audit --prod` (keine
Funde), `dotnet list package --vulnerable` (77 Projekte, keine Funde).

Die Auswertung des Paketscans ist gegen vier Fälle geprobt — echte Ausgabe
(grün), eingeschmuggelter Fund (rot), abgeschnittene Ausgabe (rot), *deutsche*
Ausgabe (rot). Der letzte Fall ist der Grund für `DOTNET_CLI_UI_LANGUAGE: en`:
lokal antwortet dasselbe Kommando deutsch, auf dem Läufer englisch, und ein
grep auf einen der beiden Sätze wäre in der jeweils anderen Umgebung still
immer wahr.

**Nicht gemessen: der Arbeitsablauf selbst.** Er ist nie auf GitHub gelaufen —
geprüft sind seine einzelnen Kommandos hier auf dieser Maschine, die YAML-Datei
parst, mehr nicht. Ob `azure/setup-helm`, die Versionen der Actions und der
GHA-Cache greifen, sagt erst der erste Lauf.

**Ebenfalls weiterhin nicht gemessen: `make k8s-up`.** Das Chart lintet und
rendert; ausgerollt wurde es nie. Diese Maschine verträgt fünfzehn Pods nicht
neben einer Testreihe.

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
- **Nach einer Gegenprobe `--no-incremental` bauen** — und zwar nach dem
  *Zurücknehmen*, nicht nur nach dem Patchen. Der inkrementelle Bau hat die
  Rücknahme inzwischen dreimal nicht bemerkt. Zweimal fiel ein Test über Code,
  der längst wieder richtig war; einmal *bestand* eine Gegenprobe, obwohl die
  Regel wirklich fehlte — der zweite Fall ist der gefährlichere, weil er wie ein
  Freibrief aussieht.
