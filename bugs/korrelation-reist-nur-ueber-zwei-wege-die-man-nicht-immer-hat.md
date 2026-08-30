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

## Stand

- [ ] gemeldet
- [ ] Girder reicht die Kennung an jedem `HttpClient` weiter
- [ ] `CorrelationIdMiddleware` setzt und liest Baggage
- [ ] eigener Handler hier entfernt
