# Auftrag: Girder trägt es, wir konfigurieren — und die Plattform spricht die Sprache der Person

> **ABGESCHLOSSEN.** Die eine Entscheidung, die hier offen stand — wo die Grenze
> zwischen einem begründenden und einem nacherzählenden Kommentar verläuft —
> ist am 12.09.2026 getroffen worden und steht unten in Phase 4.
>
> | | Stand am 12.09.2026 |
> |---|---|
> | **Phase 1** Girder 4.2.0 | **fertig**, und es wurden drei Fassungen (4.2.0/.1/.2). Der Baum steht heute auf 4.4.0. |
> | **Phase 2** Die Eigenbauten löschen | **fertig.** `Bremse.cs` und `Korrelation.cs` sind weg, `AddHttpContextAccessor()` aus acht Diensten, die Cookie-Zeile aus elf Diensten in die Grundlage. |
> | **Phase 3** Lokalisierung | **fertig.** ADR-0031, drei Kataloge (de/en/fr) mit drei Wächtertests, die Sprache liegt am Konto und nicht am Kopf, und **eine** E2E-Reise schaltet um. |
> | **Phase 4** Aufräumen — Bezeichner | **fertig** im Frontend; die Fachsprache bleibt deutsch. |
> | **Phase 4** Aufräumen — **Kommentardichte** | **GESCHLOSSEN am 12.09.2026.** Die Grenze steht unten und wortgleich in `CLAUDE.md`: *ein Kommentar verdient seinen Platz, wenn sein Fehlen jemanden einen Defekt neu einbauen ließe.* Weg 2 (`<remarks>` in ADRs) ist **abgesagt**, nicht vertagt; Weg 1 wurde selektiv gefahren — von sieben wortgleichen Kopien fielen **drei**, je Stelle entschieden. Der mechanische Schnitt bleibt zurückgenommen: er löschte die Sätze, die erklären, *warum 503 und nicht 404*. |
>
> **Wo das Ergebnis heute steht:** `Dienstgrundlage.cs` und `ocelot.json`
> (Phase 1/2), `web/src/core/i18n/kataloge/` und ADR-0031 (Phase 3).

Stand 03.09.2026. Vier Phasen. Die Reihenfolge ist begründet und nicht beliebig:
Phase 4 fasst dieselben Dateien an wie Phase 3, also kommt sie zuletzt.

---

## Woher der Auftrag kommt

Nach dem Umstieg auf Girder 4.1.0 stand die Frage im Raum, warum ein Entwickler,
der Girder installiert hat, **immer noch selbst** eine Bremse und eine
Korrelationsschicht schreibt. Die Antwort war: weil ich die *Symptome* behoben
und die Eigenbauten mit besseren Begründungen stehen gelassen habe. Das ist die
falsche Richtung, und die Begründungen hielten der Nachmessung nicht stand.

**Nachgemessen, und ich lag falsch:** Girders Bremse kann sehr wohl selektiv
sein. `DistributedRateLimitingMiddleware.CheckRateLimitsAsync` legt Zähler nur
an, wenn die jeweilige Grenze `> 0` ist, und `IsAllowed` ist
`results.Values.All(...)` — bei leerem Wörterbuch also `true`. Mit

```json
"DistributedRateLimiting": {
  "RequestsPerMinute": 0, "RequestsPerHour": 0, "RequestsPerDay": 0,
  "EndpointSpecificLimits": { "/auth/login": { "RequestsPerMinute": 20 } }
}
```

zählt **nur** der genannte Pfad. Das ist genau, was `Bremse.cs` tut — aus der
Konfiguration statt aus Code.

**Was wirklich im Weg steht**, ist etwas anderes: `Girder.Infrastructure` ist ein
Paket mit **21 Fremdabhängigkeiten** — Swashbuckle (2), OpenTelemetry (5),
Serilog (9), JWT-Bearer, TOTP, FluentValidation. Ein Gateway, das nur routet und
zwei Zwischenschichten will, erbt alles davon. **Das** ist der Grund, warum bei
uns Eigenbauten stehen, und es ist Girders Fehler, nicht unserer.

---

## Phase 1 — Girder 4.2.0

### 1.1 Das Paket aufteilen

Neues Paket `Girder.Http`: `CorrelationIdMiddleware`,
`DistributedRateLimitingMiddleware`, `ClientAddress`, `InProcessRateLimitStore`
und die Optionsklassen dazu. Abhängigkeiten: `Girder.Abstractions` und
ASP.NET — sonst nichts.

`Girder.Infrastructure` referenziert es weiter, damit `UseGirder` unverändert
bleibt.

**Die Namensräume bleiben, wie sie sind.** Ein Umzug in `Girder.Http.*` wäre
ordentlicher und würde jeden Aufrufer im Quelltext brechen — für eine
Nebenversion der falsche Preis. Der Grund gehört in die `.csproj`, sonst liest
ihn niemand.

### 1.2 Die Bremse vollständig aus der Konfiguration

- `RequestsPerMinute: 0` als dokumentierten und **getesteten** Weg zur selektiven
  Bremse. Heute ist das eine Eigenschaft des Codes, die niemand zugesagt hat.
- Sicherheitsköpfe (`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`)
  auf die 429. Sie fehlen dort, und das war schon einmal ein Prüferbefund.
- Ein Multiplikator, per Umgebungsvariable übersteuerbar — für Prüfstände, die
  sich selbst hämmern. Ein Faktor und kein Schalter: eine ausgeschaltete Bremse
  sieht man nicht mehr kaputtgehen.

### 1.3 Lokalisierung als Naht → **nach Phase 3 verschoben**

Ursprünglich hier eingeplant, beim Anfassen umgestellt: `ErrorMessageService` ist
**die einzige Datei in ganz Girder mit deutschem Text** (nachgemessen), und sie
gehört mit den Frontend-Texten zusammen übersetzt, nicht Wochen vorher. Eine
Naht vor der Lokalisierung zu veröffentlichen hieße, etwas auszuliefern, das
niemand benutzt — und beim ersten echten Gebrauch stellt sich heraus, dass sie
die falsche Form hat.

Steht deshalb jetzt in Phase 3, zusammen mit den Mails.

### 1.4 Ein Fehler aus 4.1.0

`\$\(\s*\w` in `CommandInjectionPattern` trifft `$(document)`. Gemessen an
realistischem Text einer Stellenbörse: eine jQuery-Beschreibung wird abgewiesen.
Auf Befehlswörter verengen.

### 1.5 Veröffentlichen — **erledigt, und es wurden drei Fassungen**

| | gefunden durch |
|---|---|
| **4.2.0** | Die Aufteilung, die selektive Bremse, der `$(`-Fehler |
| **4.2.1** | Beim ersten echten Einsatz: die Ausnahmelisten ließen sich **nicht leeren** (der .NET-Binder ergänzt Sammlungen und ersetzt sie nie — gemessen: `["9.9.9.9"]` wird `127.0.0.1, ::1, 9.9.9.9`), und die Vorgabekette bremste **ihre eigene Lebendprobe** |
| **4.2.2** | `X-RateLimit-*` standen nur auf der erlaubten Antwort, nicht auf der Abweisung |

**Das ist der eigentliche Ertrag dieser Phase.** Drei Fehler, alle unsichtbar,
solange die Anwendung die Bibliothek nachbaute statt sie zu benutzen. Der zweite
verdeckte dabei den ersten: die gebremste Gesundheitsprobe fiel nur deshalb nicht
auf, weil ein Ausnahmeeintrag sie rettete, den niemand entfernen konnte.

---

## Phase 2 — Die Eigenbauten löschen

Klein, und sie beweist den Punkt.

- **`Bremse.cs` weg.** Grenzen nach `ocelot.json`/`appsettings`.
  `BremsenkarteTests` prüft weiterhin, dass jeder gebremste Pfad wirklich eine
  Route hat — diese Zusage darf nicht mit der Datei verschwinden.
- **`Korrelation.cs` weg.** Girders Zwischenschicht aus `Girder.Http`.
- **`AddHttpContextAccessor()` aus acht Diensten raus** — Girders
  `HttpContextAccess` steht in der Vorgabe und macht es bereits.
- **Die Cookie-Zeile aus elf Diensten in die Grundlage.** `PostConfigure<
  JwtBearerOptions>(… AuchAusDemCookie())` stand elfmal wortgleich. Das ist keine
  Entscheidung eines Dienstes, sondern eine der Plattform — und elf Stellen für
  dieselbe Aussage sind elf Gelegenheiten, sie beim zwölften Dienst zu vergessen.
  Dann käme jeder Schalter im Browser mit 401 zurück, obwohl die Anmeldung
  aussieht, als hätte sie funktioniert.

**Nachgemessen und NICHT gefunden:** sonst nichts. `AddMemoryCache`,
`AddSerilog`, `AddCors`, `AddSwaggerGen`, `AddAuthorization`, `AddHealthChecks` —
kein Dienst registriert eines davon doppelt. `AddHttpClient` steht in neun
Diensten und ist kein Girder-Modul, sondern nötig. Der Verdacht, die Composition
Roots seien voller Doppelungen, hat sich also auf genau zwei Stellen reduziert.

---

## Phase 3 — Lokalisierung

**Zuerst ein ADR.** `CLAUDE.md` hält heute das Gegenteil fest: *„The UI is
German, but hardcoded — there is no i18n layer, and tests assert the German
literals directly."* Wer eine festgehaltene Entscheidung umstößt, schreibt den
Grund auf.

### Umfang, gemessen

| | |
|---|---|
| Frontend | ~318 sichtbare Texte in 68 `.tsx`-Dateien |
| Backend | Mails (`Postkorb.cs`, notification-service), Problemdokumente |

### Frontend

i18n-Schicht, Kataloge je Sprache. Erkennung über `navigator.language`; eine
**ausdrückliche Wahl gewinnt** und bleibt erhalten. Auswahl im Kopf.

### Girders Naht — aus Phase 1 hierher gezogen

`ErrorMessageService` (Girder.Core) verdrahtet rund zwanzig deutsche
Fehlermeldungen samt Handlungsvorschlägen fest, und `GlobalExceptionHandling`
gibt sie aus. Girders eigene README führt das seit Langem als Mangel. Girders
Vorgabe wird **neutral englisch**; `GirderModule.Localization` verdrahtet
ASP.NETs Anfrage-Lokalisierung, sodass `Accept-Language` die Kultur je Anfrage
setzt. Übersetzungen liefert die Anwendung über ihren eigenen
`IErrorMessageService` — die Schnittstelle gibt es bereits.

### Backend — der Punkt, den man übersieht

Mails gehen **asynchron** über die Outbox hinaus: die Abschlussmail einer
Löschung kommt Tage später. `Accept-Language` aus der auslösenden Anfrage gibt es
dann nicht mehr.

**Die Sprache muss am Konto liegen, nicht am Kopf.** Das ist eine
Spaltenänderung und gehört in denselben ADR. Die Outbox trägt weiterhin **keinen
Inhalt** — nur eine Kennung und eine Art; die Sprache wird beim Zustellen aus dem
Konto gelesen, nicht in die Zeile geschrieben.

### Tests

E2E- und Frontend-Tests prüfen heute deutsche Literale. Sie fahren künftig mit
**festgenagelter Sprache**, und **eine** Reise schaltet um. Ohne das wird jeder
Test bei jeder Textänderung mürbe.

---

## Phase 4 — Aufräumen

Zuletzt, weil Phase 3 dieselben Dateien anfasst.

- **Bezeichner auf Englisch — erledigt im Frontend.** Rund 1.200 Vorkommen in 87
  Dateien. Die **Fachsprache bleibt deutsch** (`Einwilligung`, `Stelle`,
  `Bewerbung`, `Marktstatus`): sie ist die Sprache dieser Domäne, steht so in
  CLAUDE.md und in jedem ADR, und ADR-0031 hält ausdrücklich fest, dass sie nicht
  Teil der Übersetzung ist.

- **Kommentare auf unter 10 % — versucht, gemessen, NICHT ausgeliefert.**

  Der Bestand ist nicht das, was der Plan annahm. Gemessen am 03.09.2026:
  9.543 Kommentarzeilen in `src/`, davon **2.811 XML-Pflichtzeilen**
  (`<summary>`, `<param>`, `<returns>`) und der Rest überwiegend begründende
  `<remarks>`. Unter 10 % (~3.100 Zeilen) hiesse: die `<summary>` jedes
  öffentlichen Members löschen. XML-Doku ist hier **keine** Bauvorgabe
  (`GenerateDocumentationFile` steht nirgends), also ginge es technisch.

  Ein mechanischer Schnitt wurde gebaut und dreimal verschieden scharf
  gemessen — behalten, was eine Messung, ein Verbot, eine Falle oder einen
  ADR-Verweis trägt:

  | Schärfe | Zeilen weg | Rest |
  |---|---|---|
  | streng behalten | 678 | 30 % |
  | Messung/Verbot/ADR | 3.092 | 21 % |
  | nur Messung/ADR | 4.138 | 20 % |

  Selbst der schärfste erreicht das Ziel nicht, und die Stichprobe hat gezeigt,
  warum er es nicht darf: **entfernt wurden unter anderem die Sätze, die
  erklären, warum 503 und nicht 404** — die Kernregel aus ADR-0020. Sie tragen
  keines der Merkmale, weil sie als Argument geschrieben sind („404 hiesse zu
  behaupten…") und nicht als Befehl.

  Der Befund ist damit: **in dieser Codebasis SIND die Kommentare überwiegend
  die Begründung.** Ein Filter kann Zierrat und Grund hier nicht trennen. Der
  Schnitt wurde zurückgenommen.

  ### Die Entscheidung, neu vermessen am 11.09.2026

  Der Bestand ist seit dem 03.09.2026 um drei Dienste gewachsen. Gemessen über
  `src/`, ohne `obj/`, `bin/` und Wanderungen:

  | | |
  |---|---|
  | Dateien | 539 |
  | Zeilen gesamt | 51.996 |
  | **Kommentarzeilen** | **16.157 — 31 %** |
  | davon `///` (XML) | 13.697 |
  | davon `//` | 2.373 (und eine gute Hälfte davon sind Begründungen im Rumpf) |
  | `<summary>`-Blöcke | 2.967 |
  | `<remarks>`-Blöcke | 1.108 |
  | **Zeilen INNERHALB von `<remarks>`** | **8.748 — 17 % des Baums, 54 % aller Kommentarzeilen** |

  Die Zahl 31 % ist gestiegen, nicht gefallen. Sie ist aber nicht der
  Gegenstand: der Befund von damals steht, und jede Nachmessung bestätigt ihn —
  **in dieser Codebasis sind die Kommentare überwiegend die Begründung**, und
  ein Filter kann Zierrat und Grund hier nicht trennen. Wer die Dichte auf eine
  Zahl senkt, senkt sie, indem er Gründe löscht.

  Deshalb liegt die Entscheidung nicht bei „wie viel Prozent", sondern bei
  **zwei Wegen, die sich gegenseitig nicht brauchen**:

  **Weg 1 — die Kopien an Aufrufstellen löschen. Klein, sicher, kein Verlust.**
  Derselbe Begründungssatz steht mehrfach wortgleich im Baum, obwohl der
  kanonische an der Sache selbst hängt. Gezählt:

  | Kopie | Stellen | Zeilen |
  |---|---|---|
  | „Konfiguration aus der Umgebung. VOR CreateBuilder…" | 15 `Program.cs` | 45 |
  | „Der Schluessel IST der Mensch…" (`Personenzeile`) | 7 Kontexte | ~35 |
  | „Kein AlsAussteller(): …" | 12 `Program.cs` | ~24 |
  | „Erst wandern, dann bedienen (ADR-0010)." | 15 `Program.cs` | 15 |

  Rund **120 Zeilen**, alle ersetzbar durch einen Verweis oder gar nichts, denn
  der Satz steht an `Umgebung.Laden`, an `Personenzeile` und in ADR-0010.
  **Die letzte Zeile ist der Grenzfall und gehört ausdrücklich entschieden:**
  „Erst wandern, dann bedienen (ADR-0010)" ist keine Kopie einer Begründung,
  sondern ein *Zeiger* auf eine — und ein Zeiger an der Stelle, wo man ihn
  braucht, ist billig.

  **Weg 2 — `<remarks>` aufgeben und die Gründe in die ADRs ziehen.** 8.748
  Zeilen. Dann liegt der Grund an EINER Stelle — aber nicht mehr dort, wo
  jemand ihn braucht, sondern dort, wo er ihn suchen muss. Das ist der Tausch,
  und er ist nicht umkehrbar: was einmal in ein ADR gewandert ist, wandert
  nicht zurück an die Zeile.

  **Was zu entscheiden ist, in einem Satz:** wo genau verläuft die Grenze
  zwischen einem Kommentar, der eine *Messung, ein Verbot oder eine Falle*
  festhält — der Wert dieses Baums — und einem, der den Code *nacherzählt*.
  Der mechanische Filter hat dreimal bewiesen, dass er sie nicht findet.

  ### Die Entscheidung, getroffen am 12.09.2026

  > **Ein Kommentar verdient seinen Platz, wenn sein Fehlen jemanden einen
  > Defekt NEU EINBAUEN ließe. Alles andere erzählt den Code nach.**

  Das ist die Grenze, nach der oben gefragt wurde, und sie steht wortgleich in
  `CLAUDE.md` unter den Konventionen — dort, wo sie jemand liest, der gerade
  einen Kommentar schreibt.

  **Weg 2 wird nie gefahren.** `<remarks>` in ADRs zu verschieben ist der
  teuerste denkbare Fehler in diesem Baum, und er ist unumkehrbar. Der Grund
  dafür ist nicht Geschmack, sondern die letzte Woche: **jeder** Defekt daraus
  entstand, weil eine Regel nicht dort stand, wo jemand sie brauchte —
  `secrets` im Schritt-`if`, die Egress-Grenze gegen einen Wert aus der
  Datenbank, das fehlende `.AsTracking()`, die 404-statt-401-Tür, deren Regel
  zwei Zeilen tiefer in derselben Datei schon aufgeschrieben war. Ein Grund in
  einer ADR ist ein Grund, den man **suchen** muss — und niemand sucht, was er
  nicht vermisst.

  **31 % Dichte ist deshalb kein Problem, das gelöst werden muss.** Dieser
  Baum lebt davon. Die Zahl steht hier als Messung und nicht als Ziel.

  **Der mechanische Filter wird nicht wieder benutzt.** Er hat dreimal
  bewiesen, dass er die Grenze nicht findet: er löschte unter anderem „warum
  503 und nicht 404" — einen Satz, der den Test oben glänzend besteht, denn
  ohne ihn baut der Nächste genau diesen Defekt neu ein. Wer ihn wieder bauen
  will, hat dieselbe Stichprobe vor sich.

  ### Weg 1, selektiv gefahren — was wirklich fiel

  Nicht pauschal, sondern je Stelle gegen den Test oben:

  | Kopie | Stellen | Entscheidung |
  |---|---|---|
  | „Konfiguration aus der Umgebung. VOR CreateBuilder…" | 15 `Program.cs` | **bleibt** — genau dort verschiebt jemand die Zeile, und dann liest die Konfiguration niemand mehr. |
  | „Kein AlsAussteller(): …" | 12 `Program.cs` | **bleibt** — verhindert eine falsche *Ergänzung*, nicht eine falsche Löschung. |
  | „Erst wandern, dann bedienen (ADR-0010)." | 15 `Program.cs` | **bleibt** — ein Zeiger ist kein Nacherzählen, und ein Zeiger an der Stelle, wo man ihn braucht, ist billig. |
  | „Der Schluessel IST der Mensch…" (`Personenzeile`) | 7 Kontexte | **drei weg, vier bleiben** — je Stelle entschieden, siehe unten. |

  **Es waren drei, nicht sieben, und das Kriterium ist gemessen.** Die sieben
  Kopien standen wortgleich, die Stellen sind es nicht:

  - **Weg (3):** `github_connections`, `portfolios`, `profiles`. In diesen
    Dateien steht **keine** Schwestertabelle mit einer eigenen
    `subject_id`/`user_id` — es gibt also keine Unstimmigkeit zu bemerken, und
    der Satz sagt nur noch einmal, was `HasKey(Id)` und `HasColumnName("id")`
    zwei Zeilen darunter zeigen. Dazu kommt die Gegenprobe aus dem Kanon:
    `Personenzeile` **nennt genau diese drei Tabellen beim Namen**. Der
    kanonische Ort deckt sie ab, die Kopie fügt nichts hinzu.
  - **Bleibt (4):** `mandates`, `notification_preferences`, `resumes`,
    `market_status`. Neben jeder dieser Tabellen steht in **derselben Datei**
    eine Schwester, die die Person als Spalte führt (`conversations`,
    `notifications`, `resume_documents`/`resume_requests`,
    `market_requests`/`transfers`). Wer die beiden vergleicht, hält die
    fehlende Spalte für vergessen und trägt sie nach — und dann stehen zwei
    Angaben über denselben Menschen nebeneinander, von denen die Löschung nur
    eine trifft. Das ist ein Defekt, den der Kommentar verhindert.

  **Die vier, die bleiben, sagen jetzt auch das.** Vorher trugen sie den Satz
  *„und damit die Zusage aus ADR-0027 für einen ganzen Dienst"* — und der war
  an genau diesen vier Stellen **falsch**: der Löschwächter findet diese
  Dienste über die Schwestertabellen ohnehin. Wahr war er nur an den drei
  Einzeltisch-Kontexten, also an denen, die jetzt weg sind. Eine Kopie altert
  eben nicht überall gleich.

  Gefallen sind damit rund **35** Zeilen statt der ~120, die Weg 1 pauschal
  versprochen hätte. Das ist die Differenz zwischen „Kopien zählen" und
  „Stellen entscheiden".

  **Phase 4 ist damit geschlossen.**

---

## Was diese Phasen NICHT tun

- **Kein Broker.** ADR-0025 gilt.
- **Kein Zwischenspeicher für Einwilligungen.** ADR-0013 gilt, und die
  Lokalisierung ändert daran nichts.
- **Keine Bewertung von Menschen.** ADR-0022 gilt unverändert.
