# Die Korrelationskennung reist nur über zwei Wege — und beide setzt man nicht immer ein

- **Girder-Fassung:** 3.0.1 (Quelltext gelesen bei `cc147e9`)
- **Gefunden beim:** D3 (Querschnitte im Betrieb), vertieft in H1
- **Art:** Lücke — nichts ist kaputt, es fehlt der Regelfall
- **Blockiert:** nein, aber die Kette bricht bei jedem Dienst-zu-Dienst-Sprung

## Was fehlt

`UseCorrelationId()` hängt in jedem Dienst und tut seine Sache richtig: Kennung
aus dem Kopf lesen oder erzeugen, in die Antwort, in `HttpContext.Items`, ins
Log. **Aber nur eingehend.** Ein ausgehender `HttpClient`-Aufruf trägt sie
nicht, und der empfangende Dienst erfindet deshalb eine neue.

Damit hat eine Kennung genau so lange Bestand wie ein Prozess. Wer sich meldet
und seine Kennung nennt, findet die eine Zeile, nicht die Kette — und dafür gibt
es sie.

## Wer sie heute weiterreicht

Es gibt genau zwei Schreiber, und beide lesen dieselbe Quelle
(`HttpContext.Items["CorrelationId"]`):

| Kanal | wer | Voraussetzung |
|---|---|---|
| HTTP ausgehend | `ServiceCommunicationManager` (Zeile 386) | man ruft ausschließlich darüber |
| Nachricht ausgehend | `CorrelationIdPublishFilter` | MassTransit |

Beides sind Entscheidungen, keine Selbstverständlichkeiten. Wer `HttpClient`
direkt benutzt — der Normalfall in .NET — bekommt nichts. Wer keinen Broker
betreibt, bekommt die zweite Hälfte auch nicht.

**Der `ServiceCommunicationManager` ist zudem kein Ersatz für einen
`HttpClient`**, siehe `statuscodes-werden-als-stoerung-behandelt.md`: er macht
aus jedem Nicht-2xx ein `null`. Gemessen, dieselben vier Endpunkte, derselbe
Prozess:

```
  Pfad                 roher HttpClient   ueber den Manager
  ------------------------------------------------------------
  /leer-aber-ok        200                Objekt
  /vierhundertvier     404                null
  /fuenfhundertdrei    503                null
  /fuenfhundert        500                null
```

Wer die Kennung will, muss also die Statuscodes aufgeben. Das ist die eigentliche
Lücke: die zwei Dinge haben nichts miteinander zu tun und sind trotzdem
gekoppelt.

## Der halbe dritte Weg

`LoggingBehavior` liest als dritten Rückfall
`Activity.Current?.GetBaggageItem("CorrelationId")` — **Baggage**, also den
Mechanismus, den .NET von selbst über Prozessgrenzen trägt.

Nur schreibt ihn niemand:

```
grep -rn "AddBaggage\|SetBaggage" src/     ->  0 Treffer
```

Stattdessen setzt `CorrelationIdMiddleware:32` ein **Tag**
(`Activity.Current?.SetTag("correlation.id", …)`). Ein Tag bleibt lokal, Baggage
reist. Der Leser existiert, der Schreiber nicht.

Und selbst wenn er existierte, käme er nicht an: `GetOrGenerateCorrelationId`
(Zeile 46–63) prüft Kopf → `TraceIdentifier` → neue Guid. **Baggage steht nicht
darin**, und `TraceIdentifier` ist immer gesetzt, also greift der zweite Zweig
und der dritte ist unerreichbar.

## Was wir vorschlagen

Ein `DelegatingHandler`, den Girder über `ConfigureHttpClientDefaults` an jeden
`HttpClient` hängt. Dreißig Zeilen, und danach hat **jeder** Girder-Benutzer die
Kette, ohne etwas zu tun:

```csharp
public sealed class CorrelationPropagationHandler(IHttpContextAccessor accessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains("X-Correlation-ID")
            && accessor.HttpContext?.Items["CorrelationId"]?.ToString() is { Length: > 0 } id)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
```

Drei Eigenschaften, die dabei zählen:

- **Ein mitgebrachter Kopf wird nicht überschrieben** — wer schon eine Kette
  führt, führt die äußere.
- **Ohne `HttpContext` passiert nichts.** Ein Hintergrunddienst hat keine
  Kennung, zu der etwas gehört; eine zu erfinden wäre eine Behauptung über einen
  Zusammenhang, den es nicht gibt.
- **Es ändert nichts an Statuscodes**, anders als der Umweg über den
  `ServiceCommunicationManager`.

Zusätzlich wäre es folgerichtig, in `CorrelationIdMiddleware` **Baggage statt
(oder neben) dem Tag** zu setzen und in `GetOrGenerateCorrelationId` als Quelle
zu prüfen — dann trägt sich die Kennung auch über Wege, die niemand
instrumentiert hat, und der Rückfall in `LoggingBehavior` wäre kein toter Zweig
mehr.

## Was es uns kostet

Bis dahin bricht unsere Kette am ersten Sprung. Gemessen in D3: eine Anfrage mit
`X-Correlation-ID: kette2-…` an `GET /profiles/{id}` stand im Protokoll von
profile-service; consent-service, im selben Atemzug gefragt, kannte sie nicht und
arbeitete unter einer eigenen.

Wir bauen den Handler deshalb bei uns — als dritten Schreiber desselben Musters,
nicht als Erfindung. Er fällt weg, sobald Girder ihn mitbringt.

## Stand — in Girder behoben, hier zu messen

- [x] gemeldet
- [x] Girder reicht die Kennung an jedem `HttpClient` weiter, Fassung: **4.0.0**
- [x] `CorrelationIdMiddleware` schreibt Baggage, und `LoggingBehavior` liest es
- [x] hier **gemessen** — H2, Messung 2
- [x] eigener Handler entfernt (er war ohnehin zurueckgenommen worden)

**Gemessen an einem echten Sprung**, nicht an einem Test, der nur prueft, dass
ein Kopf gesetzt wird: `GET /profiles/{id}` an profile-service mit blankem curl,
profile-service fragt darauf consent-service (`POST /consent/check-batch`).
Gesucht wurde in **beiden** Protokollen.

```
mit mitgeschickter Kennung
  profile-service  Starting request FremdesProfilAbfrage  with correlation kette-h2-1788173800
  consent-service  Starting request SammelpruefungAbfrage with correlation kette-h2-1788173800

ohne mitgeschickte Kennung (der Alltagsfall aus dem Browser)
  profile-service erzeugte: 0HNO79NTM5E1K:00000001
  consent-service sah:      0HNO79NTM5E1K:00000001
```

In D3 war das noch der Bruch: dieselbe Anfrage, und consent-service arbeitete
unter einer eigenen Kennung.

**Gegenprobe, damit die Messung nicht das Falsche beweist:** der Kopf koennte ja
von unserem eigenen Code kommen. `HttpEinwilligungstor` setzt an der ausgehenden
Anfrage genau eine Kopfzeile, `Authorization` — sonst keine. Die Kennung kann
also nur ueber `CorrelationIdHandler` gekommen sein, den Girder ueber
`ConfigureHttpClientDefaults` an jeden Client der Fabrik haengt. Dass das reicht,
haengt an einer Bedingung, die hier erfuellt ist: `new HttpClient(` kommt in
`src/` **nirgends** vor, alle neun Dienste registrieren `AddHttpClient()`. Ein
selbst gebauter Client bekaeme nichts.

`LoggingBehavior` las `Activity.Current?.GetBaggageItem("CorrelationId")`, und
`AddBaggage` kam in ganz Girder nicht vor — ein Leser ohne Schreiber. Geschrieben
wurde `SetTag`, und ein Etikett bleibt an dem Span, an den es geschrieben wurde.

Die Middleware schreibt jetzt beides. Gepäck braucht eine `Activity`, und ohne
eingerichtete Ablaufverfolgung gibt es keine; die Middleware beginnt in dem Fall
eine. Die Kennung zu verlieren, weil niemand OpenTelemetry eingerichtet hat, wäre
die falsche Richtung.

`CorrelationIdHandler` hängt über `ConfigureHttpClientDefaults` an jedem Client,
den die Fabrik baut — das ist das Modul `CorrelationPropagation`, und es steht in
`UseDefaults()`. Eine selbst gesetzte Kennung überschreibt er nicht, und außerhalb
einer Anfrage erfindet er keine.

Kopfname und Gepäckschlüssel liegen jetzt in `Girder.Abstractions.Observability`
an einer Stelle. Vier Dateien hielten sie vorher jede für sich — genau die
Streuung, aus der ein Leser und ein Schreiber entstehen, die sich verfehlen.
