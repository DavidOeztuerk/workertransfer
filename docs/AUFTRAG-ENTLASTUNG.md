# Auftrag: Girder trägt es, wir konfigurieren — und die Plattform spricht die Sprache der Person

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

  Was ohne Verlust ginge und eine Entscheidung braucht:
  1. **Doppelte Begründungen an Aufrufstellen.** `Umgebung.Laden()` erklärt sich
     in zwölf `Program.cs` noch einmal; der kanonische Satz steht an der Methode.
     Solche Kopien sind sicher zu löschen — sie einzeln zu finden ist Handarbeit.
  2. **`<remarks>` ganz aufgeben und die Gründe in die ADRs ziehen.** Dann liegt
     der Grund an EINER Stelle statt an der, wo man ihn braucht. Das ist die
     Entscheidung, die zu treffen ist — nicht eine Formatierungsfrage.

---

## Was diese Phasen NICHT tun

- **Kein Broker.** ADR-0025 gilt.
- **Kein Zwischenspeicher für Einwilligungen.** ADR-0013 gilt, und die
  Lokalisierung ändert daran nichts.
- **Keine Bewertung von Menschen.** ADR-0022 gilt unverändert.
