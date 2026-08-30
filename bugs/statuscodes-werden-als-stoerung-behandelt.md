# Statuscodes werden als Störung behandelt, nicht als Antwort

- **Girder-Fassung:** 3.0.1 (Quelltext gelesen bei `cc147e9`)
- **Gefunden beim:** H1, beim Versuch, die dreizehn Dienst-zu-Dienst-Aufrufe auf
  `IServiceCommunicationManager` umzustellen
- **Art:** Fehler — derselbe Denkfehler in zwei unabhängigen Bauteilen
- **Blockiert:** ja, für den Umstieg. Nicht für den Betrieb: wir rufen weiter
  selbst.

## Der gemeinsame Kern

Beide Bauteile behandeln einen HTTP-Statuscode als **gelungen oder gestört**.
In diesem System ist ein Statuscode aber eine **Aussage**, und welche, ist das
Entscheidende:

- `404` heißt bei `profile-service` *„verborgen oder nicht vorhanden"* — und
  muss sich von `503` unterscheiden.
- `503` heißt *„der Ledger schweigt"*: weder ja noch nein. Wer daraus
  „fehlgeschlagen" macht, nimmt der Antwort genau den Inhalt.
- `403` heißt *„du handelst für keine Firma"* — eine Aussage über den Aufrufer.

Wer alle drei auf `null` oder auf eine Ausnahme abbildet, hat die Antwort
weggeworfen und den Rest geraten.

## Fund 1 — `ServiceCommunicationManager` macht aus jedem Nicht-2xx `null`

`GetAsync` und `SendRequestAsync`, beide gleich
(`ServiceCommunicationManager.cs`, ~198 und ~248):

```csharp
if (response.IsSuccessStatusCode)
{
    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
    return UnwrapResponse<TResponse>(responseContent, serviceName);
}

var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
_logger.LogWarning("Request to {ServiceName} failed with status {StatusCode}: {Error}",
    serviceName, response.StatusCode, errorContent);

return default;
```

Der Aufrufer bekommt `null` — und `null` bedeutet dann zugleich *„leere
Antwort"*, *„404"*, *„403"* und *„503"*.

**Was uns das konkret kostet, zweimal:**

**Die Löschkaskade.** `HttpErasureDelivery` muss auf Nicht-2xx **werfen**
(ADR-0027): eine Zeile darf erst als zugestellt gelten, wenn der Empfänger
bestätigt hat. Über `SendRequestAsync` käme von einem Empfänger, der mit `500`
antwortet, ein `null` zurück — nicht unterscheidbar von einer erfolgreichen
leeren Antwort. `delivered_at` wäre eine Lüge, und der Vollständigkeitsbeweis
der Löschung wäre falsch. Das ist genau der Fall, für den es die Regel gibt.

**Das Einwilligungstor.** `profile-service` muss `404` (kein Zugriff) von `503`
(Ledger schweigt) unterscheiden — das eine ist eine Antwort, das andere
ausdrücklich keine. Beide kämen als `null`.

**Nebenbefund:** die Warnung schreibt `errorContent` ins Protokoll, also den
**Antwortrumpf** eines fremden Dienstes. Unsere Fehlerdokumente tragen keine
Werte (nachgemessen), aber die Bibliothek weiß das nicht — sie protokolliert
fremde Rümpfe blind.

## Fund 2 — `ResilientHttpPolicyHandler` macht aus jedem Nicht-2xx eine Ausnahme und wiederholt sie

`ResilienceExtensions.cs`, ~223:

```csharp
var response = await base.SendAsync(request, cancellationToken);

// Consider non-success status codes as failures for retry logic
if (!response.IsSuccessStatusCode)
{
    throw new HttpRequestException(
        $"HTTP request failed with status {response.StatusCode}: {response.ReasonPhrase}");
}
```

Damit wird ein `404` **dreimal wiederholt** (`MaxRetryAttempts = 3`, exponentiell
mit Jitter) und kommt dann als `HttpRequestException` an. Für eine Einwilligung,
die mit „nein" antwortet, heißt das: drei überflüssige Anfragen und am Ende eine
Ausnahme statt einer Antwort.

Ein Wiederholungsversuch dieser Bauart ist außerdem **für Schreibvorgänge
gefährlich**: er unterscheidet nicht zwischen idempotent und nicht, und `4xx`
wird durch Wiederholen nie besser.

## Warum es Girders ist

Beide Fälle stehen im Quelltext oben; sie brauchen keinen WorkerTransfer-Code,
um sichtbar zu sein. Und sie widersprechen der eigenen Zusage: der
`ServiceCommunicationManager` ist als *der* Weg für Dienst-zu-Dienst-Aufrufe
angelegt (er reicht als Einziger die Korrelationskennung weiter, Zeile 386) —
ein solcher Weg muss die Antwort durchreichen können.

## Was es uns kostet

Der Umstieg fällt aus, und damit auch das, was ihn attraktiv gemacht hätte: die
**Korrelationsweitergabe**. Sie steckt ausgerechnet in dem Bauteil, das die
Antwort wegwirft — heute bricht unsere Korrelationskette deshalb am ersten
Sprung (gemessen in D3: profile-service sieht die Kennung, consent-service nicht).

Widerstandsfähigkeit haben wir stattdessen dort gelöst, wo das Risiko wirklich
saß: **alle fünfzehn ausgehenden Aufrufstellen setzen jetzt ein eigenes
Zeitlimit** (sieben taten es nicht und liefen damit in die Vorgabe von hundert
Sekunden), und ein Test hält das fest. Wiederholung bauen wir bewusst **nicht**
pauschal ein — siehe oben, sie gehört zur Aufrufstelle und nicht über alle.

**Wenn das behoben ist**, lohnt der Umstieg sofort: `SendRequestAsync` müsste
den Statuscode durchreichen (etwa als Ergebnistyp statt `TResponse?`), und der
Wiederholungshandler dürfte nur auf `5xx`, Zeitüberschreitungen und
Verbindungsfehler wiederholen, nicht auf `4xx`.

## Stand — zwei von drei in Girder behoben

- [x] gemeldet
- [x] `SendRequestAsync`/`GetAsync` reichen den Statuscode durch, Fassung: **4.0.0**
- [x] `ResilientHttpPolicyHandler` wiederholt nur, was sich durch Wiederholen bessern kann
- [ ] **fremde Antwortrümpfe landen weiter im Protokoll** — nicht behoben, siehe unten
- [ ] Umstieg auf `IServiceCommunicationManager` vollzogen — H2 in `docs/AUFTRAG-GIRDER-4.md`

`GetAsync` und `SendRequestAsync` geben `ServiceResponse<T>` zurück: `Status`,
`Value`, und `Body` mit dem Rumpf, in dem der ferne Dienst meist sagt, was
fehlte. Nur ein Aufruf, der überhaupt keine Antwort bekam, wirft noch. Ein leerer
Erfolg und ein `404` sind damit unterscheidbar, was sie vorher nicht waren.

Wiederholt wird nur noch, was ein zweiter Versuch anders beantworten könnte: 408,
429 und 5xx ohne 501 — eine Route, die es nicht gibt, gibt es zwischen zwei
Versuchen auch nicht. Die Antwort wird dabei getragen statt beschrieben, so dass
der letzte Versuch dem Aufrufer immer noch gibt, was der ferne Dienst gesagt hat.

Zwischengespeichert wird nur noch, was gelungen ist: ein gemerkter `404` würde
weiter antworten, nachdem die Sache da ist.

**Der dritte Punkt ist offen.** Der Manager protokolliert den fremden Rumpf
weiterhin auf `Warning`:

```
_logger.LogWarning("GET to {ServiceName} answered {StatusCode}: {Body}",
    serviceName, response.StatusCode, content);
```

Vorher war es dieselbe Zeile mit `errorContent`, also unverändert schlecht. Seit
der Rumpf ohnehin in `ServiceResponse.Body` beim Aufrufer ankommt, ist er im
Protokoll nicht nur riskant, sondern überflüssig. Gehört nach Girder als eigene
kleine Meldung.
