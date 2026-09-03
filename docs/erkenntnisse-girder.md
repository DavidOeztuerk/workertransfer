# Was wir über Girder gelernt haben — und wo wir uns geirrt haben

Kurzfassung der Befunde aus der Härtung. Alles darin ist **gemessen**, nicht
gelesen; wo nur gelesen wurde, steht es dabei.

---

## Die Regel, die aus zwei Fehlern entstand

**Suche nach dem Zweck, nicht nach dem Bauteil.**

Zweimal ist genau das schiefgegangen. Bei der Korrelationsweitergabe wurde ein
`DelegatingHandler` gesucht — Girder löst es im `ServiceCommunicationManager`,
und die Suche fand eine von zehn betroffenen Dateien. Bei der Bremse wurde eine
von **drei** Ratenbegrenzungen getestet und aus ihr auf das Ganze geschlossen.

Seitdem gilt: `grep -ril <zweck>` über Girders `src/`, **alle** Treffer ansehen,
dann entscheiden. Ein kaputter Weg beweist nichts über die anderen.

Und ein zweiter Satz, der sich als ebenso wichtig erwiesen hat: **Quelltext
lesen ist kein Beweis.** Dreimal hat erst eine Messung gezeigt, was wirklich
passiert — und einmal war die erste Messung selbst wertlos, weil ein alter
Prozess auf demselben Hafen noch antwortete.

---

## Die vier Funde in Girder

### 1. Ratenbegrenzung: drei Wege, und der einzige funktionierende ist nicht verdrahtet

| Weg | verdrahtet | bremst |
|---|---|---|
| `DistributedRateLimitingMiddleware` | ja | **nein** |
| `RateLimitMiddleware` (`UseRateLimit`) | ja | **nein** |
| `RateLimitingMiddleware` | **kein Aufrufer** | **ja** |

Dazu: `ConfigureRateLimitRules` **tötet den Prozess beim Start** — die Fabrik
registriert `IRateLimitService` und löst dabei `IRateLimitService` auf, sich
selbst. Kein Protokoll, sieht aus wie ein hängender Start.

Und `ClientIdStrategy` sowie `CustomClientIdExtractor` sind **tote Optionen**:
in `src/` nirgends gelesen, nur in Girders eigenen Tests, die ihre Vorgabewerte
prüfen — und damit grün aussehen. Wer „je Herkunft" einstellt, zählt je
Benutzer.

**Der Fund, der auch eine Behebung überlebt:** alle drei glauben
`X-Forwarded-For` und `X-Real-IP` bedingungslos, und eine Vertrauensliste gibt es
in ganz Girder nicht. Acht Anfragen gegen eine Grenze von drei kamen durch — nur
weil ein Kopf mitgeschickt wurde, den der Aufrufer selbst setzt.

### 2. Statuscodes werden als Störung behandelt, nicht als Antwort

Dieselben vier Endpunkte, derselbe Prozess, derselbe Moment:

```
  Pfad                 roher HttpClient   ueber den Manager
  ------------------------------------------------------------
  /leer-aber-ok        200                Objekt
  /vierhundertvier     404                null
  /fuenfhundertdrei    503                null
  /fuenfhundert        500                null
```

`ServiceCommunicationManager` macht aus jedem Nicht-2xx ein `null`.
`ResilientHttpPolicyHandler` macht daraus eine **Ausnahme** und wiederholt sie
dreimal — ein `404` würde also dreimal nachgefragt.

Für uns ist ein Statuscode eine **Aussage**: `404` heißt „verborgen oder nicht
vorhanden", `503` heißt „der Ledger schweigt", und ein Empfänger der
Löschkaskade, der `500` antwortet, darf nicht als zugestellt gelten. Wer das
alles auf `null` abbildet, hat die Antwort weggeworfen und rät den Rest.

### 3. Die Korrelationskennung reist nur über zwei Wege

`UseCorrelationId()` ist **ausschließlich eingehend**: Kennung lesen oder
erzeugen, in die Antwort, in `HttpContext.Items`, ins Log. Kein Wort über
ausgehende Aufrufe.

Weitergereicht wird sie von genau zwei Stellen, beide lesen dieselbe Quelle:

- `ServiceCommunicationManager` (HTTP) — der, der die Statuscodes wegwirft.
- `CorrelationIdPublishFilter` (Nachrichten) — ein **MassTransit**-Filter, also
  eine Stufe in dessen eigener Pipeline. Ohne Broker gibt es ihn nicht, und mit
  Broker hilft er einem HTTP-Sprung trotzdem nicht.

Wer `HttpClient` direkt benutzt — der Normalfall in .NET — bekommt nichts.

**Der halbe dritte Weg:** `LoggingBehavior` liest als Rückfall
`Activity.Current?.GetBaggageItem("CorrelationId")` — Baggage, den Mechanismus,
den .NET von selbst über Prozessgrenzen trägt. `grep -rn "AddBaggage|SetBaggage"`
findet in Girder **und** in Skillswap null Treffer; die Middleware setzt
stattdessen ein **Tag**, und Tags bleiben lokal. Ein Leser ohne Schreiber.

### 4. Die Validierungsstufe war nie tot, nur leer

`AddCQRS` hängt `ValidationBehavior` **bereits** in jede Pipeline und ruft
**bereits** `AddValidatorsFromAssemblies`. Es steigt sofort aus, solange es
keinen Validator findet — und wir hatten null. Es fehlte keine Verdrahtung,
sondern der Inhalt.

Das ist der einzige der vier Funde, der **kein** Girder-Fehler ist.

---

## Was der Vergleich mit Skillswap zeigte

Gleiche Bauart, andere Auswahl:

| | Skillswap | WorkerTransfer |
|---|---|---|
| Girder-Module | **13** | **7** |
| Middleware | **15** | **5** |
| Dienstklienten | `IServiceCommunicationManager` | `IHttpClientFactory` |

Skillswaps Kette hält, weil seine Klienten durch den Manager gehen. Der Preis
steht in seinem eigenen Code: `UserServiceClient.ValidateUserExistsAsync` macht
aus `response != null` ein „der Nutzer existiert". Ein `500` von UserService
heißt dort also **„gibt es nicht"**. Für eine Benachrichtigung tragbar; für eine
Löschzusage nicht.

**Kein Verdrahtungsfehler bei uns — eine andere Zusammenstellung.** Aber die
gebrochene Kette ist die Folge davon, und das ist unsere Entscheidung.

---

## Wo wir uns geirrt haben

Ehrlicher Teil, weil er die nützlichste Hälfte ist.

**Gebaut, bevor gefragt war.** Die Korrelationsweitergabe wurde einmal
handgeschrieben, ohne vorher zu prüfen, ob Girder das Stück hat — und ohne dass
es im Auftrag stand. Zurückgenommen.

**Aus einem Bauteil aufs Ganze geschlossen.** Siehe Bremse.

**Missverständlich formuliert.** „18 Einstiegspunkte, 7 gerufen" klang nach
Diensten. Es sind **Module**: alle elf Dienste rufen dieselben sieben; nur das
Gateway ruft `AddWorkerTransferDefaults` gar nicht.

**„Ein Leser, den niemand schreibt"** war für Baggage richtig und als Aussage
über das Ganze falsch — der *Kopf* hat zwei Schreiber.

**Gelesen statt gemessen.** Erst auf Nachfrage entstand die Gegenüberstellung
roher HttpClient gegen Manager, und die ist der eigentliche Beweis.

---

## Vier Annahmen des Auftrags, die die Messung korrigiert hat

- **`AddInputSanitization` löst die Validierung nicht.** Es ist XSS- und
  Injektionsabwehr am HTTP-Rand und hat mit der CQRS-Pipeline nichts zu tun.
- **`AddResilience` allein wirkt nicht.** Es registriert nur Fabriken und
  umhüllt keinen `HttpClient`.
- **„Dreizehn Aufrufe ohne Zeitlimit"** — es sind **fünfzehn** Aufrufstellen,
  und **acht** hatten eines. Die anderen sieben liefen in die Vorgabe von
  `HttpClient`: hundert Sekunden.
- **Siebzehn Module** — es sind **achtzehn**.

---

## Und vier eigene Fehler, die dabei auffielen

- Das **Einwilligungstor von portfolio-service** hatte als einziges kein
  Zeitlimit, während alle anderen eines hatten. Kein Entwurf, ein Vergessen.
- **Fünf Endpunkte** antworteten auf einen kaputten Rumpf mit `500`, darunter
  `/auth/login`, wo ein fehlendes Passwort bis in den Passwortprüfer lief.
- **Jeder** Endpunkt gab bei unlesbarem JSON `500` statt `400`.
- Eine Zeile der **Routenkarte** war auf einer verschmutzten Datenbank gemessen
  und kippte je nach Zustand zwischen zwei Werten. Behoben wurde nicht die Zahl,
  sondern der Prüfstand.

---

## H2 — die vier Entscheidungen, neu gemessen (Girder 4.0.2)

Vier Dinge galten, weil Girder kaputt war. Girder ist es nicht mehr, also war
jede neu zu messen. Das Ergebnis ist **nicht** viermal „unser Eigenbau fällt
weg" — es ist zweimal ja, einmal fast, einmal nein, und die Gründe sind
verschieden.

**Die Bremse: Girder trägt, bis auf die Absage.** Vier Proben, dieselben, gegen
die `Bremse.cs` geprüft ist:

```
Grenze 3/min, je Herkunft
  echte Herkunft 10.0.0.1                     200 200 200 429 429
  dieselbe, mit gefaelschtem X-Forwarded-For  200 200 200 429 429
  X-Forwarded-For: 127.0.0.1 (die Falle)      200 200 200 429 429
  echte Loopback-Herkunft (Ausnahmeliste)     200 200 200 200 200
  dieselbe, Ausnahmeliste geleert             200 200 429 429 429
```

Die dritte Zeile ist die, auf die es ankam: der Kopf, der die Bremse früher ohne
jede Konfiguration ganz aufhob, ändert nichts mehr. `ClientAddress.Of` liest nur
noch `Connection.RemoteIpAddress`; ein weitergereichter Kopf wirkt nur hinter
einer benannten Vertrauensliste. Die Loopback-Ausnahme steht noch in der
Vorgabe — sie ist nur nicht mehr erreichbar, außer man kommt wirklich von dort.
Auch alles Übrige hält: je Herkunft, Je-Pfad-Grenzen, `X-RateLimit-*`,
`Retry-After`, und in der Vorgabekette steht sie vor `UseAuth()`.

Trotzdem bleibt `Bremse.cs`, aus einem Grund, der klein klingt und es nicht ist:
**Girders Abweisung ist `application/json` mit einem `traceId`** — kein
Problemdokument, keine Korrelationskennung, fest verdrahtet ohne Haken. Genau das
ist hier zugesagt: wer sich beschwert, ausgesperrt worden zu sein, soll eine
Kennung nennen können. Neues Ticket, und wenn es kommt, fällt die Kette.

**Was sich dabei änderte, ohne dass jemand Code anfasste:** die Begründung im
Composition Root. Sie sagte „ob Girders Fassung das auch tut, ist Messung H2".
Jetzt sagt sie, was wirklich trägt und sich nie mehr ändert — *ein Dienst hinter
dem Gateway sieht als Herkunft nur das Gateway, also alle Aufrufer als einen*.
Der alte Grund war ein Verdacht auf Girder, der neue ist Topologie. Eine
Begründung, die nicht mehr stimmt, ist schlimmer als keine.

**Die Korrelation: Girder trägt, und unser Eigenbau ist zu Recht nie entstanden.**
Gemessen an einem echten Sprung, nicht an einem Test, der nur prüft, dass ein
Kopf gesetzt wird:

```
mit mitgeschickter Kennung
  profile-service  FremdesProfilAbfrage   correlation kette-h2-1788173800
  consent-service  SammelpruefungAbfrage  correlation kette-h2-1788173800

ohne mitgeschickte Kennung — der Alltagsfall aus dem Browser
  profile-service erzeugte: 0HNO79NTM5E1K:00000001
  consent-service sah:      0HNO79NTM5E1K:00000001
```

Gegenprobe, damit die Messung nicht das Falsche beweist: unser eigener Code
könnte den Kopf ja setzen. `HttpEinwilligungstor` setzt an der ausgehenden
Anfrage genau eine Kopfzeile, `Authorization`. Und `new HttpClient(` kommt in
`src/` nirgends vor — alle neun Dienste nehmen die Fabrik, sonst hinge der
Handler an nichts.

**Communication: die Statuscodes sind heil, der Umstieg fällt trotzdem aus.**

```
  /leer-aber-ok        200 OK                    1 Aufruf am Ziel
  /vierhundertvier     404 NotFound              1
  /vierhundertdrei     403 Forbidden             1
  /fuenfhundertdrei    503 ServiceUnavailable    1
  /fuenfhundert        500 InternalServerError   1
```

Jeder Status kommt als er selbst an, und nichts wird dreimal gefragt. Der Kern
des alten Tickets ist wirklich weg. Aber drei Dinge kamen beim Messen dazu:
`Communication` verlangt `IEventBus`, und den liefert allein
`Girder.Messaging.MassTransit` — also ein Broker, den wir bewusst nicht
betreiben. `UseGateway` steht per Vorgabe auf `true`, dann geht jeder Aufruf an
den Dienst `gateway` und der übergebene `serviceName` wird für die Adresse gar
nicht gelesen. Und `EnableResponseCaching` steht auf `true` mit „jede
GET-Antwort, fünf Minuten".

Vor allem aber: **der Grund, der den Umstieg attraktiv machte, ist erledigt.**
Die Korrelationskette bekommen wir jetzt an jedem `HttpClient`, ohne die Antwort
aufzugeben. Damit bleibt vom Manager nur, was wir nicht brauchen.

**Caching: die Zusage stimmte — und nichts hielt sie.** Der Auftrag sagte, keine
Einwilligungsabfrage implementiere `ICacheableQuery`. Das stimmt, nachgemessen am
Typ über alle elf Dienste. Fünf Dienste tragen den Satz im Quelltext, und
`IAbfrage` behauptete sogar, **ein Test nagle die Abwesenheit fest**.

Den Test gab es nicht.

Die ganze Garantie hing daran, dass niemand je die eine Zeile schreibt — bei
einer Regel, deren Bruch man nicht sieht: die Person hat abgeschaltet, und fünf
Minuten lang antwortet der Dienst weiter „darf sehen". Jetzt gibt es
`tests/WorkerTransfer.Ganzes.Tests/ZwischenspeicherTests.cs`, elf Zeilen für elf
Dienste, geprüft am Typ und nicht am Quelltext. Gegengeprobt: mit einem
`ICacheableQuery` an `EinwilligungPruefenAbfrage` fällt genau eine Zeile und
nennt den Typ.

Girder hat übrigens zwei unabhängige Sperren, nicht eine: `AddCQRS` hängt die
Cache-Behaviors gar nicht erst ein, wenn nichts `ICacheableQuery` implementiert,
und das Behavior selbst tut bei allen anderen Anfragen nichts. Das ist gut
gebaut. Es ersetzt nur keinen Test bei uns.

**Nebenbefund, gefunden beim Messen und sofort behoben:** `POST /auth/register`
war die einzige der vier Anmeldetüren ohne Prüfer. Ein Rumpf ohne `displayName`
lief bis in die Datenbank und kam als **500** zurück
(`null value in column "display_name"`); ein leerer Rumpf gab **400**, während
die drei anderen Türen längst 422 gaben. Beides ist jetzt 422 mit
`invalid: <felder>`. Die Prüferklasse aus H4 sollte diese Fehlerklasse dauerhaft
schließen — sie hatte genau eine Tür ausgelassen, und es fiel nicht auf, weil
kein Test einen kaputten Registrierungsrumpf schickte.

---

## H5 — was der Prüfer fand, und was daraus wurde

Ein frischer Agent hat die Migration geprüft: Modultabelle (25 Zeilen, nicht 24
— `ResourceAuthorization` kam mit 4.0.2 dazu), Zusagen mit Fundstelle und Test,
Abnahmekriterien einzeln. Sein Urteil zur Migration selbst: sie trägt. Die vier
H2-Messungen hielten seiner unabhängigen Nachmessung stand.

Sein schwerster Fund war einer, den kein Gate hatte: **die Oberfläche kam nicht
hinein.**

### Ein Draht, zwei Dialekte

Der Draht dieser Plattform ist snake_case — neun Dienste setzen ihn ausdrücklich
mit `[JsonPropertyName]`, und `apps/web` liest und schreibt danach. Wo die
Angabe fehlt, fällt .NET auf camelCase aus `GirderModule.JsonOptions` zurück.
Dann heißt dasselbe Feld auf beiden Seiten anders.

**Warum das jahrelang unsichtbar bleibt:** bei einwortigen Feldern sind
camelCase und snake_case *dasselbe Wort* — `token`, `email`, `capability`,
`reason`. Erst ein zusammengesetzter Name geht auseinander. Und zusammengesetzte
Namen sind selten.

Vier Stellen, drei verschiedene Schadensbilder, alle gemessen:

| Feld | was passierte |
|---|---|
| `RegisterBody.DisplayName` | Spalte ist `NOT NULL` → **500** (seit H4: 422). **Niemand konnte sich über die Oberfläche registrieren.** |
| `ProfilKoerper.RemoteOk` | `bool` fällt still auf `false`. Wer „Remote möglich" ankreuzte, bekam **200** — und das Häkchen war weg. |
| `GrantBody/RevokeBody/CheckBody.SubjectId` | kam als `Guid.Empty` an → **403 „a consent belongs to its subject"**. **Der Einwilligungs-Ledger war über die Oberfläche unbedienbar** — der Dienst, auf den sich alle anderen stützen. |
| `EinwilligungsfrageV1.SubjectId` | reist *zwischen* zwei Diensten, war auf beiden Seiten camelCase und damit in sich stimmig — bis ich den Empfänger richtigstellte und den Absender nicht. Gemessen: `A SubjectId must not be empty`, profile-service meldete 503, die Kandidatenliste war leer. |

Dazu ein fünfter, anderer Art: **der Client fragte `GET /profiles`**, während die
Kandidatenliste auf `GET /candidates` liegt. `/profiles` ist ein absichtlich
toter Präfix — und die Routenkarte hatte ihn sogar als `404/404/404` notiert,
ohne zu merken, dass die Oberfläche daran hängt.

### Warum 660 grüne Tests nichts merkten

Weil sie **selbst camelCase schickten.** Zehn Testdateien, quer durch acht
Reihen. Wer gegen den Server prüft statt gegen den Vertrag, bestätigt jeden
Dialekt, den der Server gerade spricht — auch einen, den sonst niemand spricht.
Der einzige Test, der recht hatte, war `apps/web/src/routes/register.test.tsx`;
der Prüfbericht hat ihn zunächst getadelt, er „zementiere den falschen Vertrag".
Er zementierte den richtigen.

Playwright hätte es sofort gezeigt und läuft weder in `make check` noch in CI.
**Das ist die eigentliche Lücke hinter allen fünf Fehlern.**

### Und eine Begründung, die ein Symptom rechtfertigte

`docs/routenkarte.yml` hielt für zwei Endpunkte **503** fest, mit dieser
Erklärung: *„der Ledger kann über ein nicht existierendes Subjekt nichts sagen.
503 ist dann die einzig wahre Antwort."*

Das ist falsch. Der Ledger *kann* etwas sagen: keine Freigabe, also nein. `503`
heißt nicht „unbekannt", sondern „hat nicht geantwortet" — und das traf zu, weil
der interne Aufruf am Draht scheiterte. Nach der Behebung antworten beide `404`,
wie überall sonst.

Die Karte hielt also ein Symptom fest **und hatte sich einen Grund dafür
geschrieben.** Das ist die unangenehmste Art, falsch zu liegen: mit Begründung.
Der Prüfstand hat es gefunden, weil er die Absicht gegen die Wirklichkeit fährt
— aber nur, weil sich die Wirklichkeit änderte.

### Was jetzt hält

`tests/WorkerTransfer.Ganzes.Tests/DrahtvertragTests.cs`, über **21** Assemblies
— elf Api-Schichten, acht Vertragsschichten der Dienste, zwei gemeinsame. Jeder
zusammengesetzte Feldname braucht ein `[JsonPropertyName]`, **und dessen Wert
darf keinen Großbuchstaben tragen.**

Beide Hälften sind teuer erkauft:

- Ein Filter auf `IsPublic` übersah `MeldungV1` — ein `private sealed record`
  innerhalb der Endpunktklasse, auf dem Draht ganz normal gebunden.
- Ein Wächter, der nur *Anwesenheit* prüft, hätte `BenachrichtigenV1` durchgehen
  lassen: die trug die Angabe — mit dem Wert `"userId"`. Er hätte den zweiten
  Dialekt festgeschrieben statt ihn zu beenden.
- Und wer nur die Api-Schichten scannt, sieht die Vertragsschichten nicht: sie
  liegen in eigenen Assemblies. Genau dort saß der Fehler zuletzt.

Gegengeprobt: Angabe entfernen → genau eine Zeile fällt und nennt das Feld.

---

## Offen — nichts mehr, Stand 03.09.2026

Was hier als offen stand, ist mit **Girder 4.1.0** behoben. Der Abschnitt bleibt
stehen, weil das Ergebnis interessanter ist als die Liste:

**Die Abweisung der Bremse** ist jetzt `application/problem+json` und nennt die
Korrelationskennung; `traceId` steht weiter daneben, weil jemand ihn schon liest.
Die Kleinigkeiten aus derselben Messung sind mit: `EndpointSpecificLimits` ist
leer statt mit sieben Skillswap-Pfaden vorbelegt, und `ClientAddress.Of` bildet
`::ffff:a.b.c.d` auf IPv4 ab. `WhitelistedIps` trägt weiterhin Loopback — nicht
fälschbar, aber jetzt in der XML-Doku als das benannt, was es ist: eine stille
Ausnahme in genau der Umgebung, in der man die Bremse zuerst ausprobiert.

**`Bremse.cs` fällt trotzdem nicht weg**, und das ist der eigentliche Ertrag
dieser Messung: der Grund, der sie hielt, war ein Mangel und ist weg — geblieben
ist ein Unterschied in der *Gestalt*. Girders Zwischenschicht ist eine **globale**
Bremse mit Verfeinerung je Pfad; jeder Pfad ohne eigenen Eintrag bekommt die
Vorgabegrenze. Durch unser Gateway läuft auch die ganze Oberfläche. Unsere bremst
fünf benannte Pfade und rührt nichts anderes an. Ein Mangel wird behoben, eine
Gestalt bleibt — und nur die zweite Sorte Grund trägt auf Dauer.

Die vollständige Aufstellung dessen, was 4.1.0 geändert hat und was hier davon
abhing, steht in [`GIRDER-ANPASSUNGEN.md`](GIRDER-ANPASSUNGEN.md).
