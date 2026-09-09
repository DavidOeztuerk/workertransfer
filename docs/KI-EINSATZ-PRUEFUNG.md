# Prüfung des KI-Einsatzes

Stand 02.09.2026, Zweig `dotnet-migration`. Geprüft wurde **lesend**: Quelltext,
Tests, ADRs, Konfiguration. `docker`, `dotnet build` und `dotnet test` waren für
diese Prüfung gesperrt (ein anderer Agent fuhr den Stapel), deshalb steht hinter
jedem Urteil, **woran** es hängt — an einer Zeile, an einem Test, oder nur an
einem Kommentar. Wo eine Aussage nur gelesen und nicht gemessen ist, steht es
dabei.

Die Linie, um die es geht, in einem Satz: **Anforderung rein, Belege raus — nie
Mensch rein, Zahl raus.** ADR-0022 verbietet genau drei Dinge (eine Zahl über
einen Menschen samt jeder Rangfolge daraus; abgeleitete Eigenschaften ohne
Grundlage; stillschweigende Vollständigkeit) und erlaubt in seinem eigenen
Abschnitt *„Was in Phase 6 wiederkommen darf"* ausdrücklich Belege mit Herkunft,
Einwilligung zuerst und Sichtbarkeit über den Ledger.

---

## 1. Der gebaute Einsatz

Es gibt genau zwei KI-Konsumenten: `POST /profiles/me/draft` und
`POST /jobs/draft`. Beide hängen an je einem eigenen Port `IEntwerfer`.

### 1.1 Der Profilkontext trägt nichts über die Person hinaus

**Fundstelle.** `src/profile-service/WorkerTransfer.Profile.Application/Ports/IEntwerfer.cs:30-34`
— `Entwurfslage(Ueberschrift, Text, Faehigkeiten, Wunsch)`, vier Felder, ein
`record` und kein Wörterbuch. Der Handler baut ihn aus dem **gespeicherten**
Profil, nicht aus der Anfrage
(`.../Application/Entwurf/ProfiltextEntwerfen.cs:40-48`); der Aufrufer steuert
allein `Wunsch` bei. Der Endpunkt liest `handelnder.Subject` aus dem geprüften
Token und reicht ihn nur an den Speicher, nie an den Kontext
(`.../Api/ProfilEndpoints.cs:121-141`). Das Profil hat ohnehin weder Name noch
Adresse; `Ort` und `RemoteOk` existieren im Profil und stehen bewusst **nicht**
im Kontext.

**Test.** `tests/WorkerTransfer.Profile.Tests/ProfilreiseTests.cs:329-346`
(`Der_Entwurf_traegt_nichts_ueber_die_Person_hinaus`). Er prüft drei Dinge am
erzeugten Prompt: keine `SubjectId`, kein `@`, und der Wunsch kommt an.

**Urteil: belegt am Quelltext, schwach am Test.**

Zwei Einschränkungen, beide konkret:

- **Die Feldmenge ist nirgends festgenagelt.** ADR-0024 §3
  (`docs/adr/0024-worker-ai-slim-one-provider.md:59-60`) sagt wörtlich *„Ein
  Test nagelt die Feldmenge fest"*. Diesen Test gibt es im .NET-Baum **nicht**.
  `grep -rn "Entwurfslage" tests/` findet drei Stellen, alle in der Testattrappe
  (`ProfilreiseTests.cs:59`, `:62`). Kein `GetProperties()`-Vergleich wie
  `tests/WorkerTransfer.GitHub.Tests/Adr0022Tests.cs:72-83` es für `Repository`
  tut. Ein fünftes Feld — `Arbeitgeber`, `Lebenslauf`, `Marktstatus` — fiele
  heute in keinem Test auf, solange sein Wert nicht zufällig ein `@` oder die
  GUID enthält.
- **`NotContain("@")` prüft die Vorrichtung, nicht die Grenze.** Der Test
  schreibt das Profil selbst (`ProfilreiseTests.cs:120-129`, Text „Ich arbeite an
  verteilten Systemen."). Schriebe eine Person ihre eigene Adresse in ihre
  Selbstbeschreibung — was erlaubt ist, es ist ihr Text —, ginge sie zu Recht
  hinaus und der Test fiele über **korrekten** Code. Die Zusage lautet „die
  Plattform legt keine Adresse dazu", geprüft wird „im Prompt steht kein @".

### 1.2 Der Stellenkontext trägt weder Mandanten noch Firmennamen

**Fundstelle.** `src/jobs-service/WorkerTransfer.Jobs.Application/Ports/IEntwerfer.cs:34-39`
— `Anzeigenentwurf(Titel, Beschreibung, Faehigkeiten, Ort, Wunsch)`. Der
Endpunkt verlangt `Capacity.ForCompany` und reicht den Mandanten ausdrücklich
nicht weiter (`.../Api/StellenEndpoints.cs:35-52`); der Handler hat gar keinen
Zugriff darauf (`.../Application/Entwurf/AnzeigeEntwerfen.cs:25-42`).

**Test.** `tests/WorkerTransfer.Jobs.Tests/StellenreiseTests.cs:249-269` prüft,
dass die Mandanten-GUID nicht im Prompt steht.
`apps/web/src/routes/company-job-new.test.tsx:126-147` nagelt zusätzlich die
**Schlüsselmenge der Nutzlast im Browser** fest
(`["description","location","skills","title","wish"]`).
`StellenreiseTests.cs:277-281` prüft zwei Sätze der Regelmenge.

**Urteil: belegt für `tenant_id`, unbelegt für den Firmennamen.**

Kein Test erwähnt einen Firmennamen. Strukturell kann keiner hineinrutschen — es
gibt kein Feld dafür —, aber genau das ist die Aussage, die ein Feldmengentest
festhielte und ein Prompt-Test nicht. Und `ROADMAP.md:1358-1361` beschreibt für
die Python-Fassung **zwei** Tests: *„einer die `__dataclass_fields__` von
`JobDraftContext` (analog zum Test auf `DraftContext`), einer die Schlüsselmenge
der Nutzlast im Browser"*. Die Browser-Hälfte hat die Migration überlebt, die
Server-Hälfte nicht.

Nebenbefund, der in keinem Dokument steht: der Stellenprompt ist **vollständig
aufrufergesteuert** (Titel, Beschreibung, Ort, Fähigkeiten kommen aus dem Rumpf,
`StellenEndpoints.cs:48-52`), der Profilprompt bis auf den Wunsch
**servergesteuert**. Der Grund ist gut (`ROADMAP.md:1362-1364`: die Anzeige
existiert beim Schreiben noch nicht), aber die Asymmetrie ist nirgends notiert,
und sie ist der Grund, warum 1.7 unten für die Stellenseite härter zutrifft.

### 1.3 Es wird nichts gespeichert — kein Prompt, keine Antwort, kein Ledger-Eintrag

**Fundstelle.** Beide Anfragen sind `IAbfrage<string>`
(`ProfiltextEntwerfen.cs:17`, `AnzeigeEntwerfen.cs:17-22`), nicht `IBefehl` —
`TransaktionsBehavior` umhüllt nur Befehle, es geht also gar keine Transaktion
auf. Kein Handler bekommt `IPruefspur` oder `IEinwilligungstor` injiziert.
`jobs-service` hat überhaupt keine Prüfspur (`JobsInfrastructure.cs:56-77`
registriert keine).

**Test.** `ProfilreiseTests.cs:353-368` (`Der_Entwurf_hinterlaesst_keine_Spur`)
zählt die Prüfeinträge vor und nach dem Aufruf. Für `jobs-service` gibt es
keinen Gegenpart — dort gibt es aber auch keine Tabelle, in der etwas landen
könnte.

**Protokoll.** Die eingecheckten Testprotokolle belegen, dass Girders
`LoggingBehavior` nur **Gestalten** schreibt, keine Werte:
`tests/WorkerTransfer.Profile.Tests/bin/Debug/net10.0/logs/profile-service-20260902.log:53`
— `Shape of EntwurfAbfrage: {Wer: {Value: guid, IsEmpty: boolean}, Wunsch:
string(6)}`. Der Wunsch erscheint als Länge, nicht als Text.

**Urteil: belegt.** Das ist die am besten abgesicherte Zusage der Naht.

### 1.4 Was ohne Schlüssel passiert

**Fundstelle.** Der Nullentwerfer ist die **Voreinstellung**, nicht der Rückfall:
`ProfileInfrastructure.cs:79-90` und `JobsInfrastructure.cs:61-71` binden
`KeinEntwerfer`, solange `Draft:Schluessel` leer ist; `KeinEntwerfer` wirft
(`Profile/.../Entwurf/Entwerfer.cs:48-54`, `Jobs/.../Entwurf/Entwerfer.cs:41-47`).
Beide Endpunkte fangen `EntwurfNichtVerfuegbar` und antworten **503**
(`ProfilEndpoints.cs:145-152`, `StellenEndpoints.cs:57-62`). Es gibt
ausdrücklich keine Vorlage, die wie ein Vorschlag aussähe.

**Test.** Vier Ebenen, und das ist ungewöhnlich gründlich:

- Routenkarte: `docs/routenkarte.yml:189` (`/profiles/me/draft` → 503 für Person
  und Firma) und `:245` (`/jobs/draft` → 401/403/503), mit Begründung in
  `:183-185`.
- Frontend-Einheit: `apps/web/src/routes/profile.test.tsx:234-249` und
  `apps/web/src/routes/company-job-new.test.tsx:189-201` — der eigene Text bleibt
  stehen, es erscheint eine Meldung.
- E2E gegen den echten Stapel:
  `apps/web/e2e/consents-journey.spec.ts:231-255` und
  `apps/web/e2e/jobs-journey.spec.ts:190-220`.
- Der Hinweis am Knopf selbst: `apps/web/src/profile/DraftHelp.tsx:50`,
  `apps/web/src/routes/company-job-new.tsx:187`.

**Urteil: belegt.**

**Aber der Schalter ist im lokalen Stapel gar nicht angeschlossen.**
`.env.example:96-101` beschreibt `ANTHROPIC_API_KEY` als *„Der Schlüssel für die
KI-Naht (`POST /profiles/me/draft`, `POST /jobs/draft`)"*.
`docker-compose.yml` enthält **kein einziges** Vorkommen von `Draft` oder
`ANTHROPIC` — weder im Anker `*umgebung` noch bei `profile-service` (Zeilen
188-198) oder `jobs-service` (229-238). Verdrahtet ist es ausschließlich in
Kubernetes: `deploy/helm/workertransfer/templates/_helpers.tpl:127-138` bildet
`WORKER_ANTHROPIC_API_KEY` auf `Draft__Schluessel` ab, gesteuert über
`values.yaml:106-107` und `scripts/k8s-up.sh:83`.

Folge: In `docker compose` lässt sich die Naht über die dokumentierte Variable
**nicht einschalten**. Der Kommentar im E2E
(`consents-journey.spec.ts:244-247`: *„Wer lokal mit ANTHROPIC_API_KEY startet,
prüft mit diesem Test etwas anderes"*) beschreibt einen Fall, den es dort nicht
gibt. Das ist sicher in die richtige Richtung falsch — aber es ist eine Zusage
in `.env.example`, die nichts tut.

### 1.5 Fehler melden die Art, nie den Inhalt

**Fundstelle.** `HttpEntwerfer` fängt `HttpRequestException` und gibt nur den
**Typnamen** weiter (`Profile/.../Entwerfer.cs:106-113`, `Jobs/.../Entwerfer.cs:92-97`);
ein Nicht-2xx wird zur Statuszahl (`:117-121` / `:101-105`); eine unerwartete
Gestalt zu einem generischen Satz.

**Test.** ADR-0024 §4 (`docs/adr/0024-...:64-65`) behauptet: *„ein Test prüft,
dass ein Netzwerkfehler den Prompt nicht mitschleppt"*. **Diesen Test gibt es
nicht.** `grep -rn "HttpEntwerfer" tests/` liefert null Treffer — die Klasse, die
die Nutzlast zusammensetzt und die Fehlermeldungen formt, wird von keinem Test
berührt. Alle Entwurfstests ersetzen `IEntwerfer` durch eine Attrappe
(`ProfilreiseTests.cs:92`, `StellenreiseTests.cs:48`).

Was es dafür gibt: `tests/WorkerTransfer.Ganzes.Tests/ZeitlimitTests.cs` prüft
über den **Quelltext**, dass jede Aufrufstelle ein eigenes Zeitlimit setzt —
beide Entwerfer setzen dreißig Sekunden (`Entwerfer.cs:82` / `:68`).

**Urteil: teilweise verletzt.** Zwei Wege umgehen die eigene Zusage, beide am
Quelltext gelesen und nicht gemessen:

1. **Ein Zeitüberlauf ergibt 500, nicht 503.** `HttpClient.SendAsync` wirft beim
   Ablauf von `client.Timeout` eine `TaskCanceledException`, keine
   `HttpRequestException`. Der `catch` in `Entwerfer.cs:106` fängt sie nicht, sie
   steigt am Endpunkt vorbei (`ProfilEndpoints.cs:145` fängt nur
   `EntwurfNichtVerfuegbar`) und wird zum allgemeinen Fehler. Die Routenkarte
   nagelt 503 nur für den Fall „kein Schlüssel" fest, also fällt es nirgends auf.
2. **`JsonDocument.Parse` steht außerhalb jeder Zuordnung**
   (`Entwerfer.cs:123-124` / `:107-108`). Antwortet der Anbieter mit 2xx und
   keinem JSON, wirft `System.Text.Json`, und Girders `LoggingBehavior`
   protokolliert die Ausnahmemeldung samt Position — die aus dem **Rumpf des
   Anbieters** abgeleitet ist. Genau der Moment, den der Kommentar in
   `Entwerfer.cs:108-110` beschreibt („ein Fehlschlag ist genau der Moment, in
   dem jemand den Prompt sehen will"), ist nicht abgedeckt.

### 1.6 Kein Gedächtnis, kein Vektorspeicher, kein Plan-Act-Reflect

**Fundstelle/Urteil: belegt strukturell.** Ein Aufruf, ein Text
(`Entwerfer.cs:69-133`). Nirgends im Baum ein Vektorspeicher, keine
Werkzeugaufrufe, keine zweite Runde. Die Begründung, warum die Schleife fehlt,
steht ausführlich in `ROADMAP.md:1370-1381` und ist eine Entscheidung, kein
Versäumnis. Kein Test hält es fest — hier ist das in Ordnung, denn ein
zusätzlicher Aufruf wäre eine sichtbare neue Codezeile, keine stille Drift.

### 1.7 Der Fund, den keine Zusage abdeckt: `wish` ist ein offener Kanal

`EntwurfKoerper(string Wish)` (`ProfilEndpoints.cs:41`) und
`EntwurfV1(...)` (`src/jobs-service/WorkerTransfer.Jobs.Contracts/Stellenvertrag.cs:53-58`)
haben **keine Längenbegrenzung und keinen Validator**. Die einzigen
`AbstractValidator` im Baum liegen in identity, applications und github — für die
Naht existiert keiner. Die Oberfläche begrenzt auf 200 Zeichen
(`DraftHelp.tsx:53`, `company-job-new.tsx:190`), der Server auf nichts. Eine
Ratenbremse gibt es nur für fünf Auth-Pfade im Gateway; `/jobs/draft` und
`/profiles/me/draft` sind ungebremst.

Zwei Folgen, und die zweite ist die, um die es in diesem Dokument geht:

- Jeder angemeldete Aufrufer kann die Naht als allgemeinen Textgenerator auf
  Kosten des Betreibers benutzen. Bei `/jobs/draft` ist der **ganze** Prompt
  aufrufergesteuert.
- **Die Grenzklasse begrenzt, was die Plattform hinzufügt — nicht, was der
  Aufrufer hineinschreibt.** Ein Recruiter kann in `wish` Text über einen
  namentlich genannten Menschen unterbringen; `Anzeigenentwurf.Regeln`
  (`Jobs/.../IEntwerfer.cs:42-52`) verbietet dem *Modell* Aussagen über
  Bewerbende, aber der Prompt trägt den Text trotzdem hinaus. Die Zusage „er sagt
  nie etwas über jemanden" ist eine Zusage über den Dienst, nicht über die
  Leitung. Das steht in keinem Kommentar und in keinem ADR.

### Übersicht

| Zusage | Fundstelle | Test | Urteil |
|---|---|---|---|
| Profilkontext ohne Name/Adresse/SubjectId/Arbeitgeber/Lebenslauf | `Profile/.../Ports/IEntwerfer.cs:30-34`, `ProfiltextEntwerfen.cs:40-48` | `ProfilreiseTests.cs:329-346` (Prompt, nicht Feldmenge) | **teilweise belegt** — der in ADR-0024 §3 behauptete Feldmengentest fehlt |
| Stellenkontext ohne `tenant_id` | `Jobs/.../Ports/IEntwerfer.cs:34-39`, `StellenEndpoints.cs:45-52` | `StellenreiseTests.cs:249-269`, `company-job-new.test.tsx:140-146` | **belegt** |
| Stellenkontext ohne Firmennamen | dieselbe Klasse (kein Feld) | — | **unbelegt** |
| Nichts gespeichert (Prompt, Antwort, Prüfspur, Ledger) | `IAbfrage`, keine Ports injiziert | `ProfilreiseTests.cs:353-368`; Protokolle zeigen nur Gestalten | **belegt** |
| Ohne Schlüssel wird niemand gerufen, und es wird gesagt | `ProfileInfrastructure.cs:79-90`, `JobsInfrastructure.cs:61-71`, 503 | Routenkarte, 2 Frontend-Tests, 2 E2E-Reisen | **belegt** — aber in Compose gar nicht anschaltbar |
| Fehler tragen die Art, nie den Inhalt | `Entwerfer.cs:106-131` | **kein Test auf `HttpEntwerfer`** | **teilweise verletzt** (Zeitüberlauf → 500; `JsonDocument.Parse` ungefangen) |
| Kein Gedächtnis/Reflect/Ledger-Eintrag | ein Aufruf, ein Text | — (Entscheidung, keine Drift) | **belegt** |
| „Sagt nie etwas über jemanden" | Regelmenge + Kontextklasse | — | **verletzbar über `wish`** |

---

## 2. Die drei geplanten Dienste

`docs/SCOUT-UND-BERATER.md` ist ein Entwurf. Er ist gut — er nennt die Verbote
schärfer als ADR-0022 selbst und sagt an mehreren Stellen die unbequeme Hälfte
laut. Die folgenden Punkte sind die Stellen, an denen er bei einer naiven
Umsetzung kippt, plus das, was sein ADR beantworten muss, damit die Grenze im
Code steht und nicht in der Absicht.

Eine Vorbemerkung, die alle drei betrifft: **die Löschkaskade fängt einen neuen
Dienst nicht von selbst.** `tests/WorkerTransfer.Ganzes.Tests/LoeschempfaengerTests.cs:39-52`
führt die elf Dienste in einer von Hand gepflegten `TheoryData`. Ein zwölfter
Dienst mit `subject_id`-Spalten wird von diesem Wächter schlicht nicht
angesehen. Jedes der drei ADRs muss die Zeile in dieser Liste als Bedingung
nennen, sonst ist die Löschzusage ab dem Tag der Inbetriebnahme nur noch halb
eingelöst — und nichts wird rot.

### 2.1 `scout-service`

**Hält der Entwurf?** In seinen vier Auflagen ja, und die erste ist heute schon
gebaut: `/candidates` sortiert nach `GeaendertAm DESC, Id DESC`
(`src/profile-service/WorkerTransfer.Profile.Infrastructure/Persistence/EfProfilspeicher.cs:62-65`)
— stabil und sachfremd, genau wie gefordert. Die Karte zeigt keine Zahl
(`apps/web/src/candidates/CandidateCard.tsx:34-53`), und
`ProfilreiseTests.cs:306-322` prüft, dass in `/candidates` und `/profiles/{id}`
keines der Wörter `score`, `rank`, `match`, `percent`, `weight` vorkommt.

**Wo er kippt:**

1. **„Kein Prozentwert, kein ‚3/5' als einzige Ausgabe"** (`SCOUT-UND-BERATER.md:142-143`).
   *„als einzige Ausgabe"* ist ein Schlupfloch. Eine Trefferzahl neben den Namen
   ist eine Zahl über einen Menschen, und sie ist einen Tastendruck von
   `ORDER BY treffer DESC` entfernt. Das Dokument weiß das eigentlich: es
   begründet in `:296-301` sehr genau, warum `match.ts` eine Zählung tragen darf
   (es ordnet Stellen für einen Menschen) und die Scout-Ansicht nicht. Die Auflage
   muss deshalb lauten: **im Antwortvertrag gibt es kein Zahlfeld und
   serverseitig wird keine Trefferzahl gerechnet** — nicht „sie ist nicht die
   einzige Ausgabe".
2. **Die drei Herkunftsklassen existieren nur in Prosa.** Heute hat ein Profil
   genau eine Fähigkeitenliste ohne Herkunftsspalte
   (`Profil.Faehigkeiten`, gesucht in `EfProfilspeicher.cs:67-82`). Auflage 3
   („Der Suchindex kennt ausschließlich Klasse *Genannt*") ist damit im Moment
   trivial wahr und ab dem ersten Import (Lebenslauf, GitHub-Themen) einen Fehler
   von falsch entfernt. Das ADR muss **Herkunft als Eigenschaft der Fähigkeit**
   verlangen, nicht als Merkmal eines Importlaufs.
3. **`/me/scouting` widerspricht der eigenen offenen Frage 4.** `:149-152`
   verspricht der Person *„Du bist am 3. März in einer Suche von Firma X
   aufgetaucht"* — das setzt voraus, dass gespeichert wird, wer in wessen Suche
   auftauchte. `:409-411` beschließt: *„Ergebnisse werden nicht gespeichert, nur
   die Anfrage."* Beides zusammen geht nicht. Das ist der wichtigste offene Punkt
   des Entwurfs, und er nennt ihn selbst nicht als Widerspruch. Auflösbar ist er
   — als **Offenlegungsprotokoll** statt als Ergebnis-Zwischenspeicher: gespeichert
   wird `(Person, Unternehmen, Zeitpunkt)`, für die Person lesbar, für das
   Unternehmen unlesbar, und es fällt mit der Löschung. Dann lernt das Unternehmen
   nichts Neues und die Person alles. Das ADR muss es entscheiden, nicht die
   Umsetzung.
4. **„Kein Zugriff eines Unternehmens auf die eigene Belegschaft über Scout"**
   (`:387`, Reise `:360-362`) ist mit dem heutigen Modell **nicht umsetzbar**,
   und das ist kein Detail. Die Plattform hält absichtlich **keine
   Beschäftigungsbeziehung**: `Marktstatus` kennt ein `beschaeftigt`-Ja/Nein ohne
   Arbeitgeber
   (`src/transfer-service/WorkerTransfer.Transfer.Domain/Markt/Marktstatus.cs:71-80`),
   `Station.Arbeitgeber` steht **im Lebenslauf**, den genau dieser Arbeitgeber
   nicht sehen darf
   (`src/resume-service/WorkerTransfer.Resume.Domain/Lebenslaeufe/Station.cs:3-8`),
   und eine Mitgliedschaft in identity-service heißt „handelt für dieses
   Unternehmen" (Recruiter), nicht „ist dort angestellt". Die Regel umzusetzen
   hieße, genau das Datum einzuführen, das die Plattform verweigert — und eine
   Halbumsetzung wäre schlimmer als keine, weil sie einen Schutz *verspricht*.
   Der ehrliche Satz für das ADR steht schon woanders im Code:
   `MarktEndpoints.cs:13-19` und `Marktstatus.cs:54-64` — der Schutz heißt **„es
   gibt kein `:public`"**, also keine Streuung, nicht „Ausschluss des
   Arbeitgebers".
5. **`POST /scout/searches/{id}/approach` ist der eine Endpunkt, den ADR-0024
   heute verbietet.** Eine Ansprache ist ein Text **über und an einen bestimmten
   Menschen**. `ROADMAP.md:530-534` hat genau diese Umkehrung schon einmal
   geprüft und verworfen: damit ein Modell sie nützlich formuliert, müsste es
   etwas über diese Person wissen — und ohne dieses Wissen bleibt eine Floskel,
   die schlechter ist als der Satz, den ein Mensch selbst tippt. Das ADR muss
   entscheiden, ob der Entwurf ausschließlich die **selbst veröffentlichten Worte
   der Person** als Zusammenhang bekommt (die das Unternehmen ohnehin liest) — und
   dann eine eigene Kontextklasse mit Feldmengentest bekommt, nicht die
   `Anzeigenentwurf`-Klasse mit einem `if`.

**Welcher Test würde rot:**

- Ein `Adr0022Tests`-Zwilling über `Scout.Domain` und `Scout.Contracts`, mit dem
  Wortschatz aus `Adr0022Tests.cs:26-28` plus `percent`, `fit`, `treffer`,
  `anzahl`, `passung`, `probability`.
- Ein Feldmengentest auf den Treffervertrag (`GetProperties()` wie
  `Adr0022Tests.cs:74-83`): genau `{subject_id, headline, ort, remote, checkliste,
  belege, hinweis}` und kein achtes Feld.
- Ein Test über die erzeugte Abfrage: kein `ORDER BY` über etwas, das aus dem
  Abgleich stammt; zweimaliges Laden gibt dieselbe Reihenfolge (deterministischer
  Zweitschlüssel wie in `EfProfilspeicher.cs:64`).
- Ein Test, dass die Suche nur Zeilen mit Herkunft `genannt` liest — er
  erzwingt, dass die Spalte zuerst existiert.
- Ein Test, dass der Pflichthinweis (`:257-264`) **im Antwortvertrag** steht und
  nicht in der Oberfläche: nur dann überlebt er ein Redesign, und der Entwurf
  begründet in `:244-245` selbst, warum das der Punkt ist.
- Ein Gegenprobe-Test: eine Person **ohne** Belege liefert dieselbe
  Antwortgestalt wie eine mit — sonst rendert die Oberfläche irgendwann den
  leeren Kasten, den `:277-283` verbietet.

### 2.2 `advisor-service`

**Hält der Entwurf?** Die Haltung stimmt — er analysiert niemanden, er trägt
Mandat und Gespräche. Die Voreinstellungen sind die zurückhaltendsten, und der
Satz *„Ein Markt, in dem man versehentlich sichtbar ist, ist ein Leck"*
(`:182-183`) ist richtig.

**Wo er kippt:**

1. **Das Mandat wäre eine zweite Wahrheit neben dem Ledger.** ADR-0013 und
   ADR-0020: Sichtbarkeit steht **ausschließlich** im Ledger; `profile-service`
   hat kein Sichtbarkeitsfeld, und
   `tests/WorkerTransfer.Profile.Tests/ProfilTests.cs:28-40` hält das fest. Der
   Mandatsblock „Sichtbarkeit: niemand · freigegebene Unternehmen · alle" **ist**
   Sichtbarkeit. Als Spalte in advisor-service sind es zwei Quellen, die beim
   ersten Widerruf uneins sind. Das ADR muss festlegen: der Sichtbarkeitsteil des
   Mandats **wird in den Ledger geschrieben** — die Tokenform trägt das schon
   (`src/consent-service/WorkerTransfer.Consent.Domain/Ledger/Capability.cs:60-65`
   erlaubt `resume.visibility:tenant:<uuid>`) — und advisor-service hält nur, was
   keine Sichtbarkeit ist: Eintrittstermin, Spanne, Pensum, Gesprächsstufe.
2. **„Ausgeschlossene Unternehmen (namentlich)" hat im Ledger keine Entsprechung.**
   `Capability.cs:17-20`: *„Every capability in this system is a visibility."* Es
   gibt kein Verbot, nur eine Erlaubnis. Im Modus „nur freigegebene Unternehmen"
   ist eine Ausschlussliste die Abwesenheit einer Freigabe und damit
   ausdrückbar; im Modus „alle Unternehmen" bräuchte es eine neue Ledger-Handlung
   („deny"), die es nicht gibt und die die Reduktion in
   `EfConsentLedger.cs:37-45` mitträgt. Das ADR muss eines von beidem entscheiden:
   neue Handlung, oder „alle Unternehmen" gibt es nicht.
3. **Die Stufen können den Ledger unterlaufen.** „Stufe 2 → + Belege,
   Lebenslauf, Gehaltsspanne" (`:192-195`) würde eine Lebenslauf-Freigabe als
   **Nebenwirkung** eines Stufenwechsels erzeugen. Die Regel von `resume-service`
   ist aber, dass die Firma fragt und die Person antwortet, je Firma — und dass
   die Anfrage nicht die Erlaubnis ist. Das ADR muss sagen: `advance` **schreibt
   die entsprechenden Freigaben in den Ledger**, und jeder Dienst fragt weiter
   den Ledger. Die Stufe ist eine Bequemlichkeit über denselben Freigaben, nie
   ein zweites Tor.
4. **„Kein ‚gesperrt'-Hinweis" ist eine Statuscode-Regel, und es gibt sie schon.**
   `profile-service` beantwortet das mit drei Codes: 404 = verborgen *oder* nicht
   vorhanden, byteweise gleich; 403 = kein aktives Unternehmen; 503 = der Ledger
   schweigt (`ProfilEndpoints.cs:155-175`, Filter `SchweigenAbfangen` in `:59-60`).
   Ohne diese Festlegung antwortet Stufe 1 irgendwann 403 für „Lebenslauf
   vorhanden, aber nicht freigegeben" — und verrät damit, dass es einen gibt. Das
   ist genau das, was resume-service ausdrücklich verweigert.
5. **Offene Frage 3 („Trägt der Berater eine KI?") steht in der falschen
   Reihenfolge.** `ROADMAP.md:518-534` hat den Transferberater schon einmal
   verworfen, und zwar aus einem sachlichen Grund: im Transfer-Vorgang schreibt
   die Person **nirgends Freitext**; die einzigen zwei Freitextfelder gehören dem
   Unternehmen. Erst die Gespräche des Beraters schaffen diese Fläche. Also ist
   zuerst die Produktfrage zu entscheiden (schreibt die Person jetzt
   Nachrichten?), dann die KI-Frage. Umgekehrt baut man eine Naht für ein Feld,
   das es nicht gibt.
6. **„Zusammenfassungen der eigenen Lage" ist die gefährliche Hälfte** der Antwort
   auf Frage 3 (`:406-408`). Eine modellerzeugte Zusammenfassung über einen
   Menschen ist eine Produktentscheidung davon entfernt, der Gegenseite als „die
   Kandidatin in Kürze" gezeigt zu werden — und dann ist sie eine abgeleitete
   Eigenschaft ohne Grundlage (ADR-0022 §2). Wenn es sie gibt, muss sie technisch
   unfähig sein, die Sitzung der Person zu verlassen: nichts gespeichert, genau
   wie heute.

**Welcher Test würde rot:**

- Ein Test über das EF-Modell von advisor-service: **keine** Spalte, deren Name
  `sichtbar`, `visib`, `public`, `freigabe` enthält — dieselbe Form wie
  `ProfilTests.cs:28-40`. Damit steht die Sichtbarkeit nachweislich im Ledger.
- Ein Test, dass `advance` Ledger-Ereignisse erzeugt, und ein zweiter, dass nach
  einem Widerruf die höhere Stufe **leer liest, ohne dass die Stufe zurückfällt**
  — das ist die Regel „gewährt heißt: wurde einmal gewährt".
- Ein Test, dass Stufe 1 für „Lebenslauf nicht freigegeben" und „kein Lebenslauf"
  byteweise gleich antwortet, bis auf die Korrelationskennung.
- Vokabeltest über `Advisor.Domain`/`.Contracts` mit zusätzlich
  `wechsel`, `wahrscheinlich`, `probability` — ADR-0023 §4 verbietet die
  Wechselwahrscheinlichkeit ausdrücklich *„auch nicht nur intern"*.
- Eine Zeile in `LoeschempfaengerTests.Dienste` — Gespräche sind Personenzeilen.

### 2.3 `assessment-service`

**Hält der Entwurf?** Die drei Regeln sind die richtigen, und Regel 2 („Die
Person sieht die Bewertung. Immer, auch bei Absage.") ist die, die den Dienst
überhaupt baubar macht.

**Wo er kippt:**

1. **„Keine Zahl, sondern Text" ist der halbe Schutz.** Nichts hindert ein
   Unternehmen daran, eine Note **in** den Text zu schreiben und sie über seine
   eigene Liste vergangener Aufgaben wieder einzusammeln. Die Grenze, die hier
   wirklich trägt, ist die aus ADR-0026: verboten ist die **Zusammenführung**.
   Das ADR muss die *Sammelansicht* verbieten — es gibt keine Liste von
   Bewertungen über mehrere Menschen, auch nicht für das eigene Unternehmen, nur
   die eine je Vorgang.
2. **„Keine Bewertung, die den Vorgang überlebt"** (`:388`) sagt nicht, **wer
   löscht und wann**. Und die Lösung liegt in zwei Diensten: die Bewertung hier,
   die eingereichte Arbeit in `portfolio-service` (`:222`). Zwei Löschpfade für
   einen Vorgang sind zwei Gelegenheiten, den zweiten zu vergessen. Das ADR muss
   beide benennen und den neuen Dienst in die Kaskade eintragen (siehe
   Vorbemerkung).
3. **„Ablehnen, ohne dass es irgendwo vermerkt wird"** (`:236-238`) ist so nicht
   umsetzbar: ein Vorgang, der „Aufgabe gestellt" zeigt und dann nichts mehr,
   **ist** der Vermerk. Die Abwesenheit ist die Markierung. Die ehrliche Regel ist
   dieselbe, die die Plattform schon woanders benutzt: nach einer Ablehnung sieht
   der Vorgang aus wie einer, der nie begonnen wurde — byteweise.
4. **Unbezahlte Arbeit:** der Entwurf benennt den Missbrauch und antwortet mit
   „der Umfang steht vorne". Das ist keine Kontrolle, sondern eine Beschriftung.
   Eine Kontrolle wäre eine Obergrenze in der Domäne (Stunden ≤ N), eine
   jederzeitige Ablehnung ohne Folge und kein erneutes Stellen nach einer
   Ablehnung — drei Regeln, die man testen kann.

**Welcher Test würde rot:**

- Vokabeltest über `Assessment.Domain`/`.Contracts`: `note`, `punkt`, `score`,
  `rang`, `bewertungszahl`.
- Ein Test, dass es keinen Endpunkt gibt, der Bewertungen über mehr als einen
  Vorgang liefert — kein `?subject=`, keine Unternehmensliste.
- Ein Test, dass die Bewertung für jeden anderen Mandanten und in jeder Suche
  unsichtbar ist.
- Ein Test, dass die Domäne eine Stundenobergrenze erzwingt und eine Ablehnung
  keinen Zustand hinterlässt, der von „nie gestellt" unterscheidbar ist.
- Zeile in `LoeschempfaengerTests.Dienste`, plus ein Löschtest, der die Bewertung
  **und** die eingereichte Arbeit mitnimmt.

---

## 3. Weitere Agenten, die die Linie halten

Vorbedingung für alle: **die Naht existiert heute zweimal, nicht einmal.**
`IEntwerfer`, `EntwurfNichtVerfuegbar`, `Entwurfseinstellungen`, `KeinEntwerfer`
und `HttpEntwerfer` stehen fast wortgleich in profile-service und jobs-service.
ADR-0024:86-88 sagt *„Geteilt ist das **Paket**, nicht ein Vermittler"* — in .NET
wurde das Paket nicht angelegt, sondern kopiert. Ein dritter Konsument wäre die
dritte Kopie, und der Zeitüberlauf-Fehler aus 1.5 steckt schon jetzt zweimal
drin. Die getrennten **Kontextklassen** sind die Entscheidung und richtig; der
HTTP-Aufruf und die Einstellungen sind es nicht. Vor dem nächsten Agenten gehört
`WorkerTransfer.Entwurf` nach `src/shared/` — domänenneutral, transportlos, ohne
Fachlichkeit, also genau innerhalb der Sharing-Regel.

### 3.1 Anschreiben-Entwurf für die bewerbende Person

`POST /applications/draft` in applications-service.

- **Wem nützt er, in welche Richtung?** Der Person. Sie formuliert **ihren
  eigenen** Text; über niemanden wird etwas gesagt. Dieselbe Spiegelung wie
  heute zwischen `/profiles/me/draft` und `/jobs/draft`.
- **Welche Daten, wer hat sie freigegeben?** Die Stellenanzeige (öffentlich,
  `Stelle`), die eigenen Fähigkeiten und der eigene Profiltext (gehören ihr), ihr
  Wunsch. **Nicht**: Lebenslauf, Marktstatus, frühere Bewerbungen, `tenant_id`,
  Firmenname.
- **Beleg, und kann man widersprechen?** Der Text landet im Formularfeld
  `Bewerbung.Nachricht`
  (`src/applications-service/.../Domain/Bewerbungen/Bewerbung.cs:121`) und wird
  erst durch Absenden zu ihrem Text — dieselbe Mechanik wie heute
  (`profile.test.tsx:210-220`).
- **Welcher Test hält die Grenze?** Feldmengentest auf die Kontextklasse (jetzt,
  wo klar ist, dass er zweimal fehlt); ein Test, dass die Prüfspur nicht wächst;
  ein Test, dass die Anzeige **öffentlich** sein muss, bevor sie in den Prompt
  darf — sonst wird der Entwurf ein Weg, fremde Entwürfe zu lesen (die Regel gibt
  es schon: `EfStellenspeicher.cs:60`).
- **Warum er trotzdem falsch sein könnte.** Ein Anschreiben ist der eine Ort, an
  dem eine Person ihre eigenen Worte findet. Nimmt ein Modell sie ihr ab, lesen
  Unternehmen Modelltext und antworten mit Modelltext, und der einzige Unterschied
  zwischen zwei Bewerbungen verschwindet — zu Lasten genau derer, die sich sonst
  Mühe geben. `product-scope.md:25` verspricht die Funktion bereits, was ein
  schlechter Grund ist, sie zu bauen.

### 3.2 Lebenslauf auslesen — als Vorschlag, nie als Übernahme

`POST /resumes/me/suggestions`, rein vorschlagend, nichts gespeichert.

- **Richtung.** Das eigene Dokument der Person, zurück an sie. Niemand sonst
  sieht es je.
- **Daten.** Der Lebenslauf, den sie selbst hochgeladen hat. **Ohne
  `Station.Arbeitgeber`** — das ist die eine Zeile, die man leicht vergisst, und
  es ist genau das Feld, das ein jetziger Arbeitgeber nicht sehen darf
  (`Station.cs:3-8`). Der Kontext trüge Titel, Zeitraum und die
  Selbstbeschreibung, sonst nichts.
- **Beleg und Widerspruch.** *„Im Lebenslauf gefunden: Docker, Terraform. Welche
  willst du übernehmen?"* Erst der Klick macht daraus eine Nennung — genau die
  Regel aus ADR-0023 („benennt um, folgert nie"), auf einen neuen Eingang
  angewandt.
- **Test.** Der Vorschlag landet erst nach einem zweiten, ausdrücklichen Aufruf
  im Profil; nichts wird zwischengespeichert; und — sobald es die Herkunftsspalte
  gibt — die Klasse „vorgeschlagen" ist in der Suche unsichtbar.
- **Warum es falsch sein könnte.** Es baut die halbe Voraussetzung eines
  Dienstes, den niemand beschlossen hat (Herkunftsklassen für scout-service), und
  zwar ohne dessen ADR. Und der Nutzen ist klein: wer einen Lebenslauf hochlädt,
  kann drei Fähigkeiten auch tippen. Der Preis ist ein Prompt, der ein Dokument
  hinausträgt, das strenger geschützt ist als alles andere im System.

### 3.3 Ein Diskriminierungs- und Verständlichkeitshinweis für die eigene Anzeige

Erweiterung von `/jobs/draft`, kein neuer Dienst.

- **Richtung.** Anforderung rein, Kritik am **eigenen Text** raus. Über niemanden.
- **Beleg und Widerspruch.** Die beanstandete Stelle wird **zitiert**, nicht
  bewertet: „Zeile 4 nennt ein Alter." Das Unternehmen kann sie ignorieren.
- **Test.** Die Antwort darf ausschließlich Ausschnitte des eingegebenen Textes
  enthalten und keinen neuen Satz über Menschen; dazu der Vokabeltest.
- **Warum es falsch sein könnte.** Das ist eine Rechtsauskunft im Gewand einer
  Formulierungshilfe. `product-scope.md:31` verlangt für rechtsnahe Ausgaben eine
  Prüfung je Rechtsraum, und eine falsche **Entwarnung** („sieht sauber aus") ist
  schlechter als gar kein Hinweis.

### 3.4 „Warum finde ich mich nicht?" — und der ehrliche Teil: dafür braucht es kein Modell

Der beste Satz des ganzen Entwurfs steht in `SCOUT-UND-BERATER.md:288-291`:
*„Dein Profil nennt 7 Fähigkeiten. Unternehmen suchen nach dem, was hier steht —
nicht nach dem, was du kannst."* Das ist eine Zählung über den **eigenen**
Datenbestand plus ein Blick in den Ledger. Ein Sprachmodell fügt dem nichts
hinzu und würde nur die Gewissheit verwässern.

Ich führe es hier auf, weil es der Punkt ist, an dem sonst reflexhaft ein Agent
gebaut wird. **Warum es trotzdem falsch sein könnte:** es ist Suchmaschinen-
Optimierung für Menschen. Es lehrt, das Profil auf Auffindbarkeit zu trimmen,
und belohnt damit wieder die, die Zeit dafür haben — dieselbe Schieflage, die
ADR-0022 an GitHub kritisiert.

---

## 4. Was nicht gebaut werden sollte

### 4.1 Die Kandidaten-Zusammenfassung („der Bewerber in drei Sätzen")

Die meistgewünschte Funktion auf jeder solchen Plattform, und sie ist **genau**
das, was ADR-0022 §1 und §2 verbieten — nur in Prosa statt in einer Zahl. Eine
Zusammenfassung ist eine Verdichtung mit unausgesprochenen Gewichten: ein Score
ohne Nachkommastelle, und schlechter als einer, weil man ihm nicht Punkt für
Punkt widersprechen kann. Die Richtung stimmt nicht (Mensch rein, Aussage raus),
und die Person liest sie nie. `ROADMAP.md:1342` führt sie unter den vier
Firmenagenten, die aus genau diesem Grund nicht gebaut wurden.

### 4.2 Wechselbereitschaft, in jeder Verpackung

„Wechselwahrscheinlichkeit", „guter Zeitpunkt für eine Ansprache",
„Aktivitätssignal", „hat sein Profil kürzlich aktualisiert". ADR-0023 §4 nennt
das *„den gefährlichsten Punkt im ganzen ULTRAPLAN"* und verbietet es
ausdrücklich **auch nur intern**. Eine Vorhersage darüber, ob ein Mensch seinen
Arbeitgeber verlässt, ausgeliefert an Arbeitgeber, ist das Gegenteil einer
Plattform, der man seinen Marktstatus anvertraut.

**Die Hintertür ist heute schon halb offen**, und das gehört ins Scout-ADR:
`/candidates` sortiert nach `GeaendertAm` (`EfProfilspeicher.cs:63`). Ein
Unternehmen liest aus der Reihenfolge bereits ab, wer sein Profil zuletzt
angefasst hat. Das ist keine Vorhersage — aber es ist ihr Rohstoff. Die Regel muss
lauten: der Zeitstempel wird nie angezeigt und ist nie filterbar; er ist ein
Sortierschlüssel und sonst nichts.

### 4.3 Die automatisch begründete Absage

Klingt menschenfreundlich — die meisten Bewerbungen bekommen gar keine Antwort —
und ist es nicht. Der Text wäre eine Aussage über einen Menschen, erzeugt ohne
Grundlage, versendet im Namen des Unternehmens, und die Person kann ihr nicht
widersprechen, weil die Entscheidung schon gefallen ist. `product-scope.md:25`
verlangt, dass ein Mensch jeden externen Versand freigibt; ein entworfener
Absagetext, den ein Recruiter im Stapel durchwinkt, erfüllt den Buchstaben und
bricht die Zusage. Wenn überhaupt: der **Grund** wird von einem Menschen aus einer
kurzen Liste gewählt, und die Plattform schreibt keine Prosa.

### 4.4 Gehaltsempfehlung und „Marktwert"

Eine Zahl über einen Menschen, per Definition — und die Versuchung wird konkret,
sobald das Mandat aus `advisor-service` ein Feld „Gehaltsvorstellung als Spanne"
trägt (`SCOUT-UND-BERATER.md:170-171`). Der nächste naheliegende Schritt ist
„üblich sind hier 65–75 k", und der ist bereits an anderer Stelle abgelehnt: bei
den Vertragsvorlagen heißt es, eine Vorbelegung mit „üblichen Werten" *„sähe aus
wie eine Empfehlung und würde als eine gelesen"* (`ROADMAP.md:544-545`). Für ein
Gehalt gilt das doppelt, weil die Empfehlung dann in einer Verhandlung liegt, in
der die beiden Seiten ungleich stark sind.

---

## 5. Funde

**Behauptete Tests, die es nicht gibt** (drei, alle aus derselben Familie):

1. `docs/adr/0024-worker-ai-slim-one-provider.md:59-60` — *„Ein Test nagelt die
   Feldmenge fest."* Es gibt keinen Feldmengentest auf `Entwurfslage` oder
   `Anzeigenentwurf`. Der Papierweg ist `ROADMAP.md:1358-1361`: in der
   Python-Fassung gab es **zwei** Tests; die Browser-Hälfte hat die Migration
   überlebt (`apps/web/src/routes/company-job-new.test.tsx:140-146`), die
   Server-Hälfte nicht.
2. `docs/adr/0024-...:64-65` — *„ein Test prüft, dass ein Netzwerkfehler den
   Prompt nicht mitschleppt."* `HttpEntwerfer` kommt in `tests/` **nirgends** vor.
3. `docs/adr/0026-analytics-counts-events-not-people.md` — *„Ein Test hält die
   Feldmenge der Antwort fest … dieselbe Strenge wie bei `DraftContext`,
   ADR-0024."* `tests/WorkerTransfer.Applications.Tests/BewerbungsreiseTests.cs:381-403`
   prüft drei **Werte**, nicht die Feldmenge. Die Verweiskette zeigt auf einen
   Test, den es auch am Ziel nicht gibt.

**Zwei Wege am eigenen Fehlerversprechen vorbei** (am Quelltext gelesen, nicht
gemessen — Docker war gesperrt):

4. Ein **Zeitüberlauf** wirft `TaskCanceledException`, nicht
   `HttpRequestException`; der `catch` in
   `src/profile-service/.../Entwurf/Entwerfer.cs:106` und
   `src/jobs-service/.../Entwurf/Entwerfer.cs:92` fängt sie nicht. Ergebnis 500
   statt der versprochenen 503. Die Routenkarte deckt nur den Fall „kein
   Schlüssel" ab, also fällt es nicht auf.
5. `JsonDocument.Parse` (`Entwerfer.cs:123` bzw. `:107`) steht außerhalb jeder
   Zuordnung. Ein 2xx ohne JSON ergibt eine Ausnahme, deren Meldung aus dem Rumpf
   des Anbieters abgeleitet ist — und Girders `LoggingBehavior` schreibt
   Ausnahmemeldungen ins Protokoll (Muster:
   `tests/WorkerTransfer.Jobs.Tests/bin/Debug/net10.0/logs/jobs-service-20260902.log:937`).

**Absicht, die nirgends im Code steht:**

6. **Der Schlüssel ist im lokalen Stapel nicht angeschlossen.**
   `.env.example:96-101` beschreibt `ANTHROPIC_API_KEY` als den Schlüssel für
   beide Endpunkte; `docker-compose.yml` enthält kein `Draft`- und kein
   `ANTHROPIC`-Vorkommen. Verdrahtet ist es nur in Helm
   (`deploy/helm/workertransfer/templates/_helpers.tpl:127-138`).
7. **`wish` ist unbegrenzt und ungebremst** (`ProfilEndpoints.cs:41`,
   `Stellenvertrag.cs:53-58`; kein Validator, keine Bremse außerhalb der fünf
   Auth-Pfade). Die Grenzklasse begrenzt, was die Plattform hinzufügt — nicht,
   was der Aufrufer hineinschreibt. Bei `/jobs/draft` ist der ganze Prompt
   aufrufergesteuert.
8. **Die Naht steht zweimal im Baum** (zwei `IEntwerfer`, zwei
   `Entwurfseinstellungen`, zwei `HttpEntwerfer`), obwohl ADR-0024:86-88 ein
   geteiltes **Paket** vorsieht. Deshalb steckt Fund 4 auch zweimal drin.

**Widersprüche zwischen Dokumenten:**

9. `SCOUT-UND-BERATER.md:149-152` (`/me/scouting` zeigt, in wessen Suche man
   auftauchte) gegen `:409-411` (*„Ergebnisse werden nicht gespeichert"*). Beides
   zusammen geht nicht; der Entwurf nennt es nicht als Widerspruch.
10. `SCOUT-UND-BERATER.md:387` und die Reise `:360-362` verlangen, dass ein
    Unternehmen die eigene Belegschaft über Scout nicht findet. Die Plattform
    hält keine Beschäftigungsbeziehung (`Marktstatus.cs:71-80` kennt nur ein
    `beschaeftigt`-Ja/Nein; `Station.Arbeitgeber` steht im Lebenslauf; eine
    Mitgliedschaft heißt „handelt für", nicht „ist angestellt bei"). Die Regel
    ist ohne ein neues, heikles Datum nicht umsetzbar.
11. `docs/product-scope.md:7` sagt, die GitHub-Verbindung laufe über **OAuth**.
    Gebaut ist ein Nachweis über einen öffentlichen Gist
    (`src/github-service/.../Domain/Verbindungen/Verbindung.cs:74-79`), und
    `ROADMAP.md:740-742` nennt den Grund (es gibt noch keine OAuth-App). Absicht
    gegen Beschreibung, in einem Dokument, das CLAUDE.md als Regelquelle führt.
12. `docs/product-scope.md:25` verspricht KI-Entwürfe für **Bewerbungen,
    Portfolios und Vertragsvorlagen**. Keiner davon existiert. (Vertragsvorlagen
    sind in `ROADMAP.md:513-517` bewusst zurückgestellt; die beiden anderen sind
    schlicht nicht gebaut.)

**Zwei Tests, die die falsche Hälfte festhalten:**

13. `apps/web/src/routes/profile.test.tsx:204` und
    `apps/web/src/routes/company-job-new.test.tsx:119` prüfen die Zeichenfolge
    *„gehen dafür an Anthropic"*. Der Anbietername ist in der Oberfläche
    festverdrahtet (`DraftHelp.tsx:50`, `company-job-new.tsx:187`), die
    **Adresse** ist aber konfigurierbar (`Entwurfseinstellungen.Adresse`,
    `Entwerfer.cs:25`). Zeigt jemand `Draft:Adresse` woanders hin, ist der Satz am
    Knopf — der Satz, der **die Einwilligung ist** — falsch, und die beiden Tests
    bleiben grün.
14. `ProfilreiseTests.cs:344` (`prompt.Should().NotContain("@")`) prüft die
    Testvorrichtung, nicht die Grenze: schriebe eine Person ihre eigene Adresse in
    ihre eigene Selbstbeschreibung, fiele der Test über korrekten Code.
