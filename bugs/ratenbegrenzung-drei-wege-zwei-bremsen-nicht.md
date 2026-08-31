# Ratenbegrenzung: drei Wege, zwei bremsen nicht — und der dritte ist nicht verdrahtet

- **Girder-Fassung:** 3.0.1 (Quelltest gelesen bei `cc147e9`, `v3.0.0-1-gcc147e9`)
- **Gefunden beim:** Phase D1 (ein Weg), präzisiert in H1 (alle drei)
- **Art:** Fehler — mehrfach, unterschiedlicher Natur
- **Blockiert:** nein — wir bremsen selbst (`src/gateway/.../Bremse.cs`)

## Warum dieses Ticket neu geschrieben wurde

Die erste Fassung sagte „`DistributedRateLimitingMiddleware` bremst nicht" und
schloss daraus nichts über den Rest. Das war zu wenig: gesucht worden war ein
**Bauteil**, nicht der **Zweck**. `grep -ril ratelimit src/` findet in Girder
**drei** Middlewares, und sie verhalten sich verschieden. Ein kaputter Weg
beweist nichts über die anderen.

## Die drei Wege, gemessen

| Weg | in Girder verdrahtet | bremst |
|---|---|---|
| `Middleware/DistributedRateLimitingMiddleware` — `UseDistributedRateLimiting()`, außerdem `MiddlewarePipelineModule.cs:107` | ja | **nein** |
| `Security/RateLimiting/RateLimitMiddleware` — `UseRateLimit()` | ja | **nein** |
| `Middleware/RateLimitingMiddleware` — **kein Aufrufer in ganz Girder** | **nein** | **ja** |

Der einzige, der bremst, ist der, den niemand erreicht. Die beiden, die man
laut Doku benutzt, lassen alles durch.

### Weg 1 — `DistributedRateLimitingMiddleware`

Acht Anfragen gegen eine Grenze von fünf: achtmal `200`, keine einzige
`X-RateLimit-*`-Kopfzeile. Auch mit Girders **eigenen** sieben
Voreinstellungen (`/api/auth/login`, 5/min). Die Optionen binden korrekt
(nachgesehen), und der Speicher darunter zählt korrekt (nachgesehen):

```
SPEICHER Aufruf 1: {"IsAllowed":true,  "CurrentCount":1,"Limit":2}
SPEICHER Aufruf 2: {"IsAllowed":true,  "CurrentCount":2,"Limit":2}
SPEICHER Aufruf 3: {"IsAllowed":false, "CurrentCount":2,"Limit":2}
```

Der Fehler sitzt zwischen einem korrekt gebundenen Optionsobjekt und einem
korrekt zählenden Speicher.

```csharp
// nur Girder, kein WorkerTransfer
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();
builder.Services.AddDistributedRateLimiting(builder.Configuration);
var app = builder.Build();
app.UseDistributedRateLimiting();
app.MapPost("/api/auth/login", () => Results.Ok());
// 8x POST -> 8x 200, erwartet 5x 200 dann 429
```

### Weg 2 — `RateLimitMiddleware`, und drei Fehler auf einmal

**a) Er bremst nicht.** Regel angemeldet (`GetRegisteredRules()` sagt 1),
Middleware in der Kette, Grenze 3/min — sechs von sechs kommen durch. Auch mit
einer Regel **ohne** Bedingung, damit kein Bedingungsfehler die Messung
erklären kann.

**b) `ConfigureRateLimitRules` tötet den Prozess beim Start**, ohne eine Zeile
Protokoll:

```csharp
services.AddSingleton(provider =>
{
    var rateLimitService = provider.GetRequiredService<IRateLimitService>();
    foreach (var rule in builder.Rules)
        rateLimitService.RegisterRuleAsync(rule).Wait();
    return rateLimitService;
});
```

Die Fabrik registriert `IRateLimitService` und **löst dabei `IRateLimitService`
auf** — sich selbst. Der Prozess stirbt, bevor irgendetwas geloggt wird; er
sieht von außen aus wie ein hängender Start. Isoliert belegt: dieselbe Anwendung
ohne diese eine Zeile startet normal.

**c) `ClientIdStrategy` und `CustomClientIdExtractor` sind tot.** Beide sind
öffentlich, dokumentiert und werden in `src/` **nirgends gelesen**:

```
src/.../RateLimitMiddleware.cs:578:  public ClientIdStrategy ClientIdStrategy { get; set; } = ...
src/.../RateLimitMiddleware.cs:583:  public Func<HttpContext, string>? CustomClientIdExtractor { get; set; }
tests/.../RateLimitModelsTests.cs:   (nur Vorgabewerte und Enum-Mitglieder)
```

`GetClientId` ist fest verdrahtet auf `user:` → `apikey:` → `ip:`. Wer
`IpAddressOnly` einstellt, glaubt, je Herkunft zu zählen, und zählt je Benutzer.
Girders eigene Tests prüfen die Vorgabewerte der Option und sehen damit grün
aus.

### Weg 3 — `RateLimitingMiddleware`: bremst, aber niemand hängt ihn ein

Von Hand eingehängt (`app.UseMiddleware<RateLimitingMiddleware>()`, denn Girder
tut es nirgends) bremst er wirklich:

```
Grenze 3/min:  200 200 200 200 429 429
```

Zwei Anmerkungen: **einer zu viel** kommt durch (vier statt drei), und es gibt
keine Erweiterungsmethode — `grep -rn "RateLimitingMiddleware>" src/` findet nur
seine eigenen Zeilen.

## Der Fehler, der alle drei betrifft: der Kopf des Aufrufers wird geglaubt

Alle drei lesen die Herkunft so:

```csharp
var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
if (string.IsNullOrEmpty(ipAddress))
    ipAddress = context.Request.Headers["X-Real-IP"].FirstOrDefault();
if (string.IsNullOrEmpty(ipAddress))
    ipAddress = context.Connection.RemoteIpAddress?.ToString();
```

**Ohne jede Vertrauensliste.** `grep -rn "KnownProxies|TrustedProxy|ForwardedHeaders|TrustProxy" src/` findet in
ganz Girder **nichts**. Diese Köpfe setzt jeder Aufrufer selbst — wer sie glaubt,
gibt dem Angreifer den Schlüssel des Zählers in die Hand. Am funktionierenden
Weg 3 gemessen:

```
Grenze 3/min, acht Anfragen mit rotierendem X-Forwarded-For:
  200 200 200 200 200 200 200 200
```

Acht von acht durch. Die Bremse sieht von außen aus wie eine Bremse und hält
nichts auf.

Ein weitergereichter Kopf ist erst dann etwas wert, wenn eine **vertrauenswürdige**
Kette ihn setzt — und dann muss die Bibliothek wissen dürfen, welche Kette das
ist. Solange sie das nicht anbietet, darf sie den Kopf nicht lesen.

## Was es uns kostet

Wenig, und deshalb bleibt hier nichts rot: **die Zähler sind das Wertvolle, und
die funktionieren.** `src/gateway/WorkerTransfer.Gateway/Bremse.cs` setzt auf
`IDistributedRateLimitStore.SlidingWindowIncrementAsync` — nachgemessen richtig —
und bringt nur die Kette darüber selbst mit.

Das ist **kein Umweg um den Fehler**, sondern eine andere, funktionierende
Schnittstelle derselben Bibliothek. Und es ist kein Nachbau: keiner der drei
Wege könnte die zwei Zusagen halten, an denen für uns alles hängt —

- **je Herkunft, nie je Benutzer**: Weg 2 kann das nicht (tote Option), Weg 1 und
  3 nur über eine Einstellung, die den Kopf trotzdem glaubt;
- **den Kopf des Aufrufers nicht glauben**: kein Weg kann das, weil Girder
  nirgends eine Vertrauensliste kennt.

**Wenn das behoben ist**, lohnt der Vergleich neu — Weg 3 mit einer echten
Vertrauensliste wäre schlanker als unserer. Der Wechsel darf nur nicht die
Zusagen kosten.

## Stand — in Girder behoben, hier zu messen

- [x] gemeldet
- [x] von drei Wegen bleibt einer, Fassung: **4.0.0**
- [x] `ConfigureRateLimitRules` gibt es nicht mehr
- [x] die Einstellung, wen gezählt wird, wird gelesen
- [x] weitergereichte Köpfe nur hinter einer erklärten Vertrauensliste
- [x] eigene Bremse hier **gemessen** — H2, Messung 1
- [ ] eigene Bremse entfernt — haengt an
  `abweisung-der-bremse-ist-kein-problemdokument.md`, nicht mehr an diesem Ticket

**Gemessen an Girder 4.0.2, ohne Fremdcode.** Alles, was hier stand, ist zu:

```
Grenze 3/min, je Herkunft
  1) echte Herkunft 10.0.0.1                          200 200 200 429 429
  2) dieselbe, mit gefaelschtem X-Forwarded-For        200 200 200 429 429
  3) X-Forwarded-For: 127.0.0.1 (die Falle)            200 200 200 429 429
  4) echte Loopback-Herkunft (Ausnahmeliste)           200 200 200 200 200
  4b) dieselbe, Ausnahmeliste geleert                  200 200 429 429 429
```

Zeile 2 und 3 sind der Punkt: der Kopf aendert nichts mehr, auch nicht der, der
frueher die Bremse ganz aufhob. `ClientAddress.Of` liest allein
`Connection.RemoteIpAddress`; weitergereichte Koepfe wirken nur ueber eine
benannte Vertrauensliste (`TrustForwardedHeadersFrom`), und ohne sie gar nicht.
Zeile 4 gegen 4b zeigt, dass die Loopback-Ausnahme noch da ist — sie ist aber
nicht mehr erreichbar, ausser man kommt wirklich von dort.

Auch die uebrigen Zusagen halten: je Herkunft (drei Adressen, drei Toepfe),
Je-Pfad-Grenzen (`/auth/login` auf 2 gebremst, `/jobs` bei 100 nicht),
`X-RateLimit-Limit/-Remaining/-Reset` auf jeder Antwort, `Retry-After` auf der
Abweisung, und `UseRateLimiting()` steht in der Vorgabekette vor `UseAuth()`.

Die eigene Bremse bleibt trotzdem — aus einem neuen und viel kleineren Grund,
der nichts mit diesem Ticket zu tun hat: die Abweisung ist
`application/json` mit einem `traceId` und damit weder Problemdokument noch mit
Korrelationskennung. Das steht jetzt in einem eigenen Ticket.

In den elf Diensten bleibt das Modul draussen, und dort ist der Grund
endgueltig: ein Dienst hinter dem Gateway sieht als Herkunft nur das Gateway.

Die Behebung war nicht „alle drei bremsen", sondern **einer bleibt**.

`RateLimitMiddleware` fragte `IRateLimitService`, dessen `CheckRateLimitAsync`
über eine Regelsammlung läuft — leer, solange niemand Regeln einträgt, und dann
kommt `IsAllowed = true` heraus. Der einzige Weg, Regeln einzutragen, war
`ConfigureRateLimitRules`, und das registriert eine Singleton-Fabrik für
`IRateLimitService`, die im Rumpf `IRateLimitService` auflöst: sich selbst. Wer
sie je auflöste, verlor den Prozess ohne Protokoll. Dass es nie auffiel, steht in
seinem eigenen Test — er prüft, dass eine Registrierung *hinzugefügt* wurde, und
löst sie nie auf. `RateLimitingMiddleware` zählte in einem `IMemoryCache`, also je
Prozess, und war nirgends verdrahtet.

Entfernt: `IRateLimitService` samt beiden Anbieterfassungen, `AddRateLimit`,
`AddRateLimitMiddleware`, `ConfigureRateLimitRules`, beide überzähligen
Middlewares und ihr Regelmodell.

`ClientIdStrategy` und `CustomClientIdExtractor` standen auf einer Optionsklasse
und wurden nirgends gelesen — wer „je Herkunft" einstellte, zählte je Benutzer.
An ihrer Stelle steht `RateLimitSubject`, auf jeder Anfrage gelesen, mit
`SubjectExtractor` für Mandant oder Schlüssel.

**Der schärfste Fall stand nicht im Ticket:** `IsWhitelisted` las die
Ausnahmeliste über denselben gefälschten Kopf, und die Liste trägt per Vorgabe
`127.0.0.1`. Ein `X-Forwarded-For: 127.0.0.1` hob die Bremse damit ohne jede
Konfiguration ganz auf. Dieselbe Blindheit lag an sechs weiteren Stellen —
Prüfprotokoll, Telemetrie, Eingabesäuberung.
