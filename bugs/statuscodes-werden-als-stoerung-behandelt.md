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

## Stand — in Girder behoben, hier zu messen

- [x] gemeldet
- [x] `SendRequestAsync`/`GetAsync` reichen den Statuscode durch, Fassung: **4.0.0**
- [x] `ResilientHttpPolicyHandler` wiederholt nur, was sich durch Wiederholen bessern kann
- [x] fremde Antwortrümpfe landen nicht mehr im Protokoll, Fassung: **4.0.1**
- [x] hier **gemessen** — H2, Messung 3
- [ ] Umstieg auf `IServiceCommunicationManager` — **abgesagt**, Begruendung unten

**Gemessen an Girder 4.0.2, ohne Fremdcode.** Ein Ziel, das mitzaehlt, wie oft es
gefragt wurde; der Aufrufer geht ueber `IServiceCommunicationManager`:

```
  Pfad                 Status kommt an           Aufrufe am Ziel
  --------------------------------------------------------------
  /leer-aber-ok        200 OK                    1
  /vierhundertvier     404 NotFound              1
  /vierhundertdrei     403 Forbidden             1
  /fuenfhundertdrei    503 ServiceUnavailable    1
  /fuenfhundert        500 InternalServerError   1
```

Beide Fragen sind damit beantwortet: **jeder Status kommt als er selbst an**, und
**nichts wird dreimal gefragt**. Der Kern dieses Tickets ist wirklich weg.

Wiederholt wird ueberhaupt kein Statuscode mehr, auch kein 5xx: der Manager
haengt `ResilientHttpPolicyHandler` gar nicht an seinen Client, sondern nutzt
`IRetryPolicy` unter dem Namen `ServiceCommunication`, und deren `ShouldRetry`
prueft **Ausnahmen**. Seit ein Nicht-2xx keine Ausnahme mehr ist, loest nur noch
ein Transportfehler oder eine Zeitueberschreitung eine Wiederholung aus. Fuer uns
ist das die richtige Richtung: ein `503` heisst *„der Ledger schweigt"* und darf
nicht still nachgefragt werden, bis irgendwann etwas anderes herauskommt.

## Warum der Umstieg trotzdem nicht kommt

Nicht mehr wegen der Statuscodes — die sind in Ordnung. Drei andere Gruende,
alle beim Messen gefunden:

**1. `Communication` verlangt einen Broker.** `CommunicationModule` deklariert
`RequiresProvider<IEventBus>`, und `IEventBus` registriert in ganz Girder genau
eine Stelle: `Girder.Messaging.MassTransit`. Ohne Anbieter stirbt der Container
beim Aufloesen des Managers:

```
Unable to resolve service for type 'Girder.Abstractions.Messaging.IEventBus'
while attempting to activate 'ServiceCommunicationManager'.
```

Wir betreiben bewusst keinen Broker. Den Manager zu nehmen hiesse, MassTransit
und RabbitMQ mitzunehmen, damit `PublishEventAsync` existiert — eine Methode, die
wir nicht rufen.

*(Der Wortlaut oben ist der rohe DI-Fehler, weil meine Probe den Manager von Hand
aufloeste. Girders eigener Waechter ist ein `IStartupFilter` und meldet sich mit
Namen des fehlenden Anbieters und dem Aufruf, der ihn liefert, sobald der Wirt
wirklich startet. Das ist gut gebaut — es hat mich nur nicht erwischt.)*

**2. `UseGateway` steht per Vorgabe auf `true`.** Dann geht **jeder** Aufruf an
den Dienst namens `gateway`; der `serviceName`, den man uebergibt, wird fuer die
Adresse nicht gelesen. Bei uns liefe Dienst-zu-Dienst-Verkehr damit durch Ocelot
und durch unsere eigene Bremse. Gemessen: mit der Vorgabe kam
`Service ziel not configured`, obwohl `ServiceEndpoints:ziel` gesetzt war.

**3. `EnableResponseCaching` steht per Vorgabe auf `true`**, und die Vorgabepolitik
ist „jede GET-Antwort, fuenf Minuten". Unsere Einwilligungspruefung ist ein POST
(`/consent/check-batch`) und entkaeme dem — aber nur durch ihre **Form**, nicht
durch eine Entscheidung. Ein Ledger-Ergebnis, das fuenf Minuten liegen bleibt,
waere ADR-0013 ins Gesicht: eine Ruecknahme muss beim naechsten Lesen wirken.
Siehe Messung 4.

**Und der Grund, der den Umstieg ueberhaupt attraktiv machte, ist weg:** die
Korrelationsweitergabe. Die haengt seit 4.0.0 an jedem `HttpClient` der Fabrik
(Messung 2, nachgewiesen ueber einen echten Sprung). Wir bekommen die Kette also,
ohne die Antwort aufzugeben — genau das, was hier als „die eigentliche Luecke"
stand.

Ein Nebenbefund: der Manager liest die Dienstadressen aus dem **obersten**
Abschnitt `ServiceEndpoints`, waehrend `ServiceCommunicationOptions` eine eigene
Eigenschaft `ServiceEndpoints` unter `ServiceCommunication` hat, die dabei
niemand liest. Zwei Orte fuer dieselbe Sache, einer davon tot.

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

**Der dritte Punkt ist mit 4.0.1 erledigt.** Der Manager protokollierte den
fremden Rumpf auf `Warning`, und zwar an drei Stellen: beim GET, beim POST, und
die Fehlerliste aus einem `success:false`-Umschlag — die letzte auf einem `200`,
also auf dem Pfad, den man am ehesten übersieht.

```
vorher   GET to UserService answered NotFound: {"error":"anna@example.com hat kein Konto"}
jetzt    GET to UserService answered NotFound (48 bytes)
```

Protokolliert wird die Form: welcher Dienst, welcher Status, wie viele Bytes
beziehungsweise wie viele Fehler. Die Werte kommen ohnehin in
`ServiceResponse.Body` beim Aufrufer an, wo entschieden werden kann, ob sie
behalten werden dürfen.
