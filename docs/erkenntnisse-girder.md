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

## Offen

Die Kette bricht weiterhin am ersten Sprung. Der Vorschlag steht in
`bugs/korrelation-reist-nur-ueber-zwei-wege-die-man-nicht-immer-hat.md`: ein
`DelegatingHandler` über `ConfigureHttpClientDefaults` — dreißig Zeilen, und
danach hat **jeder** Girder-Benutzer die Kette, ohne etwas zu tun. Dazu Baggage
setzen statt nur ein Tag.

Bis dahin wäre derselbe Handler bei uns der dritte Schreiber desselben Musters —
keine Erfindung, und wieder löschbar, sobald Girder ihn mitbringt.
