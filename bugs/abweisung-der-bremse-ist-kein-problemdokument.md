# Die Abweisung der Bremse ist kein Problemdokument und trägt keine Korrelationskennung

- **Girder-Fassung:** 4.0.2
- **Gefunden beim:** H2, Messung 1 — der Rückbau der eigenen Bremse
- **Art:** Lücke — die Bremse ist richtig, ihre Antwort passt nicht zum Rest
- **Blockiert:** nein. Es kostet genau den Rückbau, um den es ging.

## Was passiert

`DistributedRateLimitingMiddleware.HandleRateLimitExceeded` schreibt die 429
selbst, fest verdrahtet (`DistributedRateLimitingMiddleware.cs:360–395`):

```csharp
context.Response.ContentType = "application/json";

var response = new
{
    type = "https://tools.ietf.org/html/rfc6585#section-4",
    title = "Too many requests",
    status = 429,
    detail = "Rate limit exceeded. Please try again later.",
    instance = context.Request.Path.Value,
    traceId = context.TraceIdentifier,
    …
};
```

Gemessen, Girder 4.0.2, kein Fremdcode:

```
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 60

{"type":"…rfc6585#section-4","title":"Too many requests","status":429,
 "detail":"Rate limit exceeded. Please try again later.","instance":"/auth/login",
 "traceId":"0HNO79KOLPVG1:00000001", …}
```

Zwei Dinge daran passen nicht zu Girders eigenem Rest:

**1. `application/json` statt `application/problem+json`.** Der Rumpf *ist* ein
Problemdokument — er trägt `type`, `title`, `status`, `detail`, `instance`. Nur
der Inhaltstyp sagt es nicht. Ein Aufrufer, der auf `problem+json` prüft, um
Fehler von Nutzlast zu unterscheiden, sieht hier gewöhnliches JSON.

**2. `traceId` statt der Korrelationskennung.** Girder hat seit 4.0.0 eine
Kennung, die über Prozessgrenzen reist — `CorrelationIdMiddleware` schreibt sie
in Kopf, `HttpContext.Items` und Gepäck, `CorrelationIdHandler` trägt sie an
jedem `HttpClient` weiter, und der Name liegt zentral in
`Girder.Abstractions.Observability`. Ausgerechnet die eine Antwort, die ein
Mensch am ehesten meldet — *„ich bin ausgesperrt"* — nennt sie nicht, sondern
`HttpContext.TraceIdentifier`, der je Verbindung gilt und nirgends sonst
auftaucht.

Die Middleware steht in `UseSharedInfrastructure` **vor** `UseAuth()` und damit
auch hinter `UseCorrelationId()` (Zeile 184 gegen 192) — die Kennung liegt zum
Zeitpunkt der Abweisung längst bereit.

## Warum es Girders ist

Beides steht im Quelltext oben und braucht keinen fremden Code. Und es
widerspricht der eigenen Zusage: `UseExceptionHandling` liefert für jeden
anderen Fehler ein Problemdokument mit Korrelationskennung; die Bremse ist der
einzige Weg, auf dem ein Aufrufer eine Fehlerantwort in anderer Form bekommt —
und zwar auf dem Pfad, der am häufigsten nach außen zeigt.

## Was es uns kostet

Den Rückbau. Alles andere an der Bremse ist nachgemessen in Ordnung: sie bremst,
sie zählt exakt, sie zählt je Herkunft (`PerOrigin()`), sie liest
`X-Forwarded-For` nicht mehr, ein gefälschtes `X-Forwarded-For: 127.0.0.1` hebt
sie nicht auf, sie kann Je-Pfad-Grenzen, und sie setzt `X-RateLimit-Limit`,
`-Remaining`, `-Reset` und `Retry-After`. Damit erfüllt sie beide Zusagen, an
denen für uns alles hängt — je Herkunft nie je Adresse, und weiter außen als die
Authentifizierung.

Bleiben musste unsere Kette (`src/gateway/WorkerTransfer.Gateway/Bremse.cs`)
allein wegen der Antwortgestalt: `Die_Abweisung_nennt_kein_Konto` nagelt
`application/problem+json` und ein nicht leeres `correlationId` fest.

## Vorschlag

Die Middleware sollte dieselbe Gestalt schreiben wie `UseExceptionHandling`:
`application/problem+json`, und die Korrelationskennung aus
`Girder.Abstractions.Observability` statt `TraceIdentifier` — oder zusätzlich zu
ihm, dann bleibt beides.

Alternativ ein Haken (`RateLimitingBuilder.Rejecting(Func<HttpContext, …>)`), der
die Antwort der Anwendung überlässt. Der erste Weg ist besser: die Gestalt der
Fehlerantwort ist nichts, worüber jede Anwendung neu entscheiden sollte.

## Nebenbefund, nicht dringend

Zwei Kleinigkeiten aus derselben Messung:

- **`WhitelistedIps` trägt per Vorgabe `127.0.0.1` und `::1`.** Das ist heute
  nicht mehr fälschbar, seit die Herkunft nur aus `Connection.RemoteIpAddress`
  kommt — der scharfe Fall von damals ist also wirklich zu. Es bleibt aber eine
  stille Ausnahme für jeden Aufrufer, der wirklich von Loopback kommt, und die
  überrascht in genau der Umgebung, in der man die Bremse zuerst ausprobiert:
  auf dem eigenen Rechner sieht sie aus, als bremse sie nicht. Gegengeprobt mit
  `Exempting()` — dann bremst sie auch dort.
- **`EndpointSpecificLimits` ist mit sieben fremden Pfaden vorbelegt**
  (`/api/auth/login`, `/api/admin/*`, …). Konfiguration *ergänzt* das Wörterbuch,
  sie ersetzt es nicht. Gemessen: `POST /api/auth/register` wurde bei 3 gebremst,
  obwohl die Anwendung diese Route gar nicht kennt. Eine leere Vorbelegung wäre
  ehrlicher; die heutige ist Skillswaps Landkarte in einer Bibliothek.
- **`ClientAddress.Of` normalisiert `::ffff:a.b.c.d` nicht.** Derselbe Rechner
  bekommt unter beiden Schreibweisen zwei Töpfe, also die doppelte Grenze.
  Gemessen, aber ich halte es für harmlos: welche Form gilt, entscheidet der
  Lauscher, nicht der Aufrufer.

## Stand

- [ ] gemeldet
- [ ] die Abweisung ist `application/problem+json` und nennt die Korrelationskennung
- [ ] eigene Kette hier entfernt
