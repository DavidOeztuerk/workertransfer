# ADR-0030: Die Sammelprüfung — eine Runde statt *n*, ohne einen einzigen Cache

**Status:** angenommen (13.08.2026)
**Betrifft:** `worker-contracts`, consent-service, profile-service
**Verwandt:** ADR-0013 (synchron lesen, nicht zwischenspeichern), ADR-0004 (versionierte Verträge)

## Der Anlass, gemessen

`/candidates` blieb im Ladezustand stehen — „Profile werden geladen…", und zwar
dauerhaft. Zwei E2E-Reisen waren deshalb rot bzw. wackelig, und die Beweisbilder
zeigten als einzigen Inhalt der Seite `status: Profile werden geladen…`.

Gefunden beim Einlösen der Gateway-Beweise. Der ausführliche Befund samt der
irreführenden Trace-Spur (`GET /profiles` → **200**, während das DOM weiter „lädt"
zeigte — die Antwort kam, nachdem die Behauptung aufgegeben hatte) steht in
`docs/befund-kandidatenliste-haengt.md`, eingebracht mit dem Zweig
`e2e-mailpit-zeitlimit`.

Die Ursache war keine Langsamkeit der Datenbank, sondern die **Anzahl der
Runden**. `profile-service` filtert eine Seite Kandidaten, indem es für jede
Zeile den Ledger fragt — und `may_see` fragt **zwei** Fähigkeiten je Person
(öffentlich, dann unternehmensspezifisch). Eine Seite von 20 Profilen waren damit
bis zu **40 HTTP-Aufrufe**, jeder mit eigenem `httpx.AsyncClient` und damit
eigenem Verbindungsaufbau.

Gemessen auf ruhiger Maschine, 130 freigegebene Profile, 345 Zeilen im Ledger:

| | vorher | nachher |
|---|---|---|
| eine Seite `GET /profiles` | **1,7 – 8,8 s** | **0,015 – 0,083 s** |
| Aufrufe an den Ledger je Seite | bis 40 | **1** |
| Einträge, Reihenfolge, Cursor | — | **identisch** |

Unter der Last eines vollen E2E-Laufs überschritt das die 30 Sekunden, nach denen
die Oberfläche aufgibt. Zwei Reisen waren deshalb rot bzw. wackelig.

## Die Entscheidung

`POST /consent/check-batch` beantwortet bis zu `MAX_CHECK_BATCH = 100` Paare
`(subject_id, capability)` in **einer** Anfrage. `profile-service` benutzt ihn für
die Liste; die einzelne Prüfung (`GET /profiles/{id}`) bleibt unverändert.

## Was sich ausdrücklich NICHT ändert

**Es wird nichts zwischengespeichert.** ADR-0013 verbietet das, und die
Begründung gilt unverändert: ein Widerruf muss beim **nächsten** Lesen wirken,
nicht beim übernächsten. Eine Sammelprüfung ist eine Frage in einer Runde, keine
Vorratsantwort — sie fragt genauso synchron wie vorher, nur einmal statt
vierzigmal. Wer hier später einen Cache einzieht, hebelt ADR-0013 aus, und die
gewonnene Zeit wäre kein Argument dafür: sie ist schon gewonnen.

**Der Zugang wird nicht weiter.** `/check` ist für jeden authentifizierten
Aufrufer über jede Person offen — das macht den Ledger als Enabler brauchbar. Die
Sammelprüfung ändert daran nichts Grundsätzliches; sie macht das Fragen aber
**billiger**, und das ist ein ehrlich zu benennender Unterschied. Deshalb die
Obergrenze: aus einer Anfrage werden 100 Antworten, nicht 100.000. Die Zahl ist
hergeleitet und nicht gewählt — die größte Seite in `profile-service` sind 50
Profile, bei zwei Fähigkeiten je Person also 100 Paare. Ein Test hält beides
zusammen (`MAX_PAGE_SIZE * 2 <= MAX_CHECK_BATCH`), damit die Grenze nicht genau
dort reißt, wo sie am meisten hilft.

**Der Widerrufsgrund bleibt draußen.** `ConsentCheckBatchResultV1` trägt dieselben
Felder wie `ConsentCheckResultV1`, also kein `reason`. Eine Sammelprüfung darf
nichts preisgeben, was die einzelne verschweigt.

## Drei Dinge, die tragen und leicht wegzuräumen sind

- **Die Antworten kommen in der Reihenfolge der Fragen.** Das ist Vertrag, nicht
  Bequemlichkeit: der Aufrufer ordnet sie seinen Zeilen zu. Käme sie beliebig,
  müsste er über `(subject_id, capability)` zuordnen — und ein doppelt gefragtes
  Paar wäre dann nicht mehr eindeutig. `profile-service` prüft die Länge der
  Antwort gegen die Länge der Frage und wirft sonst `ConsentUnavailable`: falsch
  zuzuordnen hieße, das Profil der falschen Person zu zeigen.
- **`may_see_many` fragt immer beide Fähigkeiten**, nicht erst die öffentliche und
  die zweite nur bei Bedarf. In einer gemeinsamen Anfrage kostet die zweite Frage
  nichts mehr; die Alternative wäre eine zweite Runde für genau die Personen ohne
  öffentliche Freigabe — ein Aufwand, der mit der Anzahl der *nicht*
  Freigegebenen steigt und damit selbst eine Auskunft wäre.
- **Ein ungültiges Paar lässt die ganze Anfrage scheitern.** Nicht „an dieser
  Stelle nicht erteilt": eine unlesbare Kennung ist ein Programmierfehler beim
  Aufrufer, und als fehlende Einwilligung ausgegeben wäre er unsichtbar — bis
  jemand eine ganze Seite lang niemanden mehr sieht und die Ursache im Ledger
  sucht. Dieselbe Regel gilt für „mehr Paare als der Vertrag trägt".

## Was der zweite Weg kostet

Es gibt jetzt **zwei** Wege an dieselbe Auskunft, und zwei Wege, die sich uneinig
werden können, sind schlimmer als kein zweiter Weg. Dagegen stehen drei Tests:

1. Im Handler: derselbe Fake, dieselben Ereignisse, Paar für Paar dasselbe Urteil
   — über alle vier Lagen, die `project_state` unterscheidet (erteilt,
   widerrufen, gelöscht, nie berührt).
2. Im Repository gegen echtes Postgres: die `IN`-Liste über Wertepaare ist eine
   **andere** SQL-Abfrage als die mit zwei Gleichheiten. Dass sie dieselbe
   Reduktion anwendet, ist eine Eigenschaft des `DISTINCT ON` samt `ORDER BY` und
   keine des Python-Codes darüber. Der Fall „widerrufen und danach erneut
   erteilt" ist dabei der, an dem eine falsche Ordnung auffällt.
3. Im Client: die Anzahl der HTTP-Aufrufe, nicht die Laufzeit. Eine Zeitmessung im
   Test wäre eine Aussage über die Maschine.

## Was offen bleibt

**Kein Client der Oberfläche hat ein Zeitlimit.** Das ist der zweite Teil des
Befunds und hier nicht behoben: `fetch` wartet von sich aus unbegrenzt, und eine
Seite, die deshalb endlos „lädt", ist für eine Person von Langsamkeit nicht zu
unterscheiden. Nach dieser Änderung ist der Anlass weg, der Mangel nicht.
