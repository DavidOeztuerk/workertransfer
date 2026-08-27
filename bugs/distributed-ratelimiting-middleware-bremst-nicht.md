# `DistributedRateLimitingMiddleware` lässt jede Anfrage durch — auch weit über der eigenen Grenze

- **Girder-Fassung:** 3.0.1
- **Gefunden beim:** Phase D1, die Bremse an den Auth-Endpunkten
- **Art:** Fehler
- **Blockiert:** nein — der *Speicher* darunter arbeitet korrekt, wir setzen unsere eigene Zwischenschicht darauf

## Was passiert

`AddDistributedRateLimiting(configuration)` + `UseDistributedRateLimiting()`,
mit einem registrierten `IDistributedRateLimitStore`, bremst **nichts**. Weder
die globale Grenze noch eine pfadgenaue, und auch nicht die sieben Grenzen, die
Girder selbst als Voreinstellung mitbringt.

Gemessen gegen eine frisch gestartete Instanz:

| Aufruf | Grenze | 8 Anfragen ergaben |
|---|---|---|
| `POST /api/auth/login` | **5/min** (Girders eigene Voreinstellung) | 8× `200` |
| `POST /auth/login` | **3/min** (selbst konfiguriert) | 5× `200` |
| `GET /jobs` | **2/min** global (`RequestsPerMinute: 2`) | 5× `200` |

Es kommt auch **keine einzige `X-RateLimit-*`-Kopfzeile** zurück, und im
Protokoll steht auf `Trace` nichts von der Zwischenschicht.

Erwartet war nach der zweiten (bzw. dritten, fünften) Anfrage ein `429`.

Die Einstellungen **binden korrekt** — das ist nachgesehen, nicht vermutet:

```
GEBUNDEN: Enabled=True Ip=True User=False Endpoint=True Sliding=True
          proMinute=2 proStunde=2 proTag=2 Sondergrenzen=8
```

Und der Zähler darunter **arbeitet richtig**:

```
SPEICHER Aufruf 1: {"IsAllowed":true,  "CurrentCount":1,"Limit":2,"RemainingRequests":1}
SPEICHER Aufruf 2: {"IsAllowed":true,  "CurrentCount":2,"Limit":2,"RemainingRequests":0}
SPEICHER Aufruf 3: {"IsAllowed":false, "CurrentCount":2,"Limit":2,"RemainingRequests":0}
```

Der Fehler sitzt also **zwischen** einem korrekt gebundenen Optionsobjekt und
einem korrekt zählenden Speicher: in der Zwischenschicht selbst.

## Warum es Girders ist

Der Fall reproduziert ohne eine Zeile WorkerTransfer. Ein leeres
`Microsoft.NET.Sdk.Web`-Projekt mit `Girder.InMemory` und
`Girder.Infrastructure` (beide 3.0.1):

```csharp
// nur Girder, kein WorkerTransfer
using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();
builder.Services.AddDistributedRateLimiting(builder.Configuration);

var app = builder.Build();
app.UseDistributedRateLimiting();
app.MapPost("/api/auth/login", () => Results.Ok(new { ok = true }));
app.Run();

// 8x POST /api/auth/login  ->  8x 200, erwartet: 5x 200 dann 429
// (5/min ist Girders EIGENE Voreinstellung fuer diesen Pfad, es braucht
//  dafuer nicht einmal eine appsettings.json)
```

Mit dieser `appsettings.json` gilt dasselbe für eine selbst gesetzte Grenze:

```json
{
  "DistributedRateLimiting": {
    "Enabled": true,
    "EnableIpRateLimiting": true,
    "EnableEndpointSpecificLimiting": true,
    "UseSlidingWindow": true,
    "RequestsPerMinute": 2,
    "EndpointSpecificLimits": {
      "/auth/login": { "RequestsPerMinute": 3 }
    }
  }
}
```

**Eine Falle beim Nachstellen:** ein noch laufender Vorgänger auf demselben
Hafen beantwortet die Aufrufe weiter, und dann misst man die alte Fassung.
Erst `lsof -ti :<hafen> | xargs kill -9`, dann neu starten. Genau darauf bin
ich hier einmal hereingefallen — die ersten Messungen waren wertlos.

## Nebenbefund, eigentlich ein zweiter Fehler

`AddInMemoryRateLimiting()` aus `Girder.InMemory` registriert **nicht**, was
`UseDistributedRateLimiting()` braucht. Wer beide Hälften desselben Merkmals
zusammensteckt, bekommt beim Start:

```
System.InvalidOperationException: Unable to resolve service for type
'Girder.Abstractions.Caching.IDistributedRateLimitStore' while attempting to
activate 'Girder.Infrastructure.Middleware.DistributedRateLimitingMiddleware'.
```

`AddInMemoryRateLimiting()` registriert den regelbasierten `IRateLimitService`,
die Zwischenschicht braucht den `IDistributedRateLimitStore` — zwei Untersysteme
mit fast gleichen Namen, die nicht zusammenpassen. Der Speicher muss von Hand
registriert werden (`InMemoryRateLimitStore` liegt in `Girder.InMemory.Caching`).
Das ist nirgends gesagt und der Name legt das Gegenteil nahe.

## Was es uns kostet

Wenig, und das ist der Grund, warum hier nichts rot bleibt: **der Zähler ist
das Wertvolle, und der funktioniert.** Wir setzen unsere eigene, sehr kleine
Zwischenschicht (`src/gateway/WorkerTransfer.Gateway/Bremse.cs`) auf
`IDistributedRateLimitStore` und benutzen `SlidingWindowIncrementAsync`
unmittelbar.

Das ist **kein Umweg um den Fehler**, sondern eine andere, funktionierende
Girder-Schnittstelle: wir bauen nichts zurück, verstecken nichts und schwächen
keine Prüfung ab. Der Gewinn bleibt derselbe, den Girder hier verspricht — der
Sprung auf einen geteilten Zähler ist ein Registrierungswechsel:
`RedisDistributedRateLimitStore` (Girder.Redis) erfüllt dieselbe Schnittstelle,
`SlidingWindowIncrementAsync` inklusive.

Was wir dabei **nicht** bekommen und selbst schreiben mussten: die
`X-RateLimit-*`-Kopfzeilen, `Retry-After`, das RFC-9457-Dokument und die
Entscheidung, welche Pfade überhaupt gebremst werden.

**Wenn dieser Fehler behoben ist**, lohnt der Vergleich: Girders
Zwischenschicht kennt Whitelists, Endpunktmuster und Strafzeiten, die wir
bewusst nicht nachgebaut haben. Ein Wechsel wäre dann eine Vereinfachung —
aber nur, wenn sie weiterhin **je Herkunft und nie je E-Mail-Adresse** zählt.
`EnableUserRateLimiting` ist genau die Einstellung, die das kippen würde.

## Stand

- [ ] gemeldet
- [ ] in Girder behoben, Fassung: <…>
- [ ] eigene Zwischenschicht hier entfernt
