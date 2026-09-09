# Die Korrelationskennung steht in der Datei, aber nicht auf der Konsole

- **Girder-Fassung:** gefunden in 4.2.2, **behoben in 4.3.0**
- **Gefunden beim:** Messen am laufenden `docker compose`-Stapel (03.09.2026)
- **Art:** Lücke
- **Blockiert:** nein — die Kennung reist korrekt; sie ist nur dort unsichtbar,
  wo im Betrieb jemand hinsieht
- **Status:** ✅ **geschlossen am 09.09.2026**, nachgemessen auf 4.4.0 (unten)

## Was passiert

Eine Anfrage mit gesetzter Kennung durch das Gateway an consent-service:

```
curl -s -X POST localhost:8090/consent/check \
  -H 'X-Correlation-ID: PROBE2-1788452696' \
  -H 'Content-Type: application/json' -d '{}'
```

| Wo | Ergebnis |
|---|---|
| Antwortkopf `X-Correlation-ID` | ✅ `PROBE2-1788452696` |
| `correlationId` im Problemdokument | ✅ `PROBE2-1788452696` |
| Logdatei im Container | ✅ `CorrelationId: PROBE2-1788452696` |
| **Konsole / stdout** (`docker compose logs`) | ❌ **kein Treffer** |

Erwartet: die Kennung steht in jeder Logzeile, die zu dieser Anfrage gehört —
auf stdout, weil das im Container das Protokoll IST.

## Warum es Girders ist

`LoggingConfiguration` hängt `Enrich.FromLogContext()` an, und
`CorrelationIdMiddleware` legt die Kennung per `BeginScope` an jedes Ereignis.
Die Eigenschaft liegt also an. Nur nennt die Konsolenvorlage sie nicht:

```csharp
// Girder.Infrastructure/Logging/LoggingConfiguration.cs, ApplyDefaultSinks
if (environment.IsDevelopment())
{
    loggerConfig.WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}"
                       + "{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code);
}
```

Die **Dateisenke** derselben Methode tut es ausdrücklich:

```csharp
outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] "
               + "{SourceContext}: {Message:lj}{NewLine}{Exception}"
               + "{NewLine}    CorrelationId: {CorrelationId}"
               + "{NewLine}    ServiceName: {ServiceName}{NewLine}";
```

Zwei Senken, dieselbe Anfrage, zwei verschiedene Antworten auf die Frage „wozu
gehört diese Zeile".

Reproduktion ohne WorkerTransfer-Code:

```csharp
// nur Girder
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) =>
    LoggingConfiguration.Configure(cfg, ctx.HostingEnvironment, "probe"));

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapGet("/", (ILogger<Program> log) => { log.LogInformation("hier"); return "ok"); });
app.Run();

// GET / mit `X-Correlation-ID: XYZ`
// stdout: [12:00:00 INF] Program: hier          <- keine Kennung
// logs/probe-*.log:  ...  CorrelationId: XYZ    <- da ist sie
```

## Was es uns kostet

Der Faden reisst genau da, wo man ihn braucht. `shared/api/fehler.ts` sagt über
die Kennung:

> Die Kennung ist der einzige Faden, an dem eine Beschwerde durch alle Dienste
> zurückverfolgbar ist.

Im Betrieb liest man Protokolle mit `docker compose logs` oder `kubectl logs`,
und beide lesen stdout. Die Datei **im** Container erreicht niemand, und sie
fällt mit dem Container. Wer also eine `correlationId` aus einem Bildschirmfoto
bekommt, findet dazu nichts — obwohl alles richtig verdrahtet ist.

**Umweg:** die Konsolenvorlage im Anwendungscode überschreiben. Er kostet, dass
jede Anwendung Girders Vorlage kennen und pflegen muss — genau die Sorte Kopie,
gegen die Girder gebaut ist. Deshalb steht hier ein Ticket und kein Eigenbau.

**Vorschlag:** `{CorrelationId}` in die Entwicklungsvorlage, hinter dem Level
und vor dem `SourceContext`, z. B.

```
"[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}"
```

Serilog lässt eine fehlende Eigenschaft leer, also schadet es keinem Ereignis
ausserhalb einer Anfrage. Für die Produktion ist nichts zu tun: dort schreibt
`JsonFormatter()` ohnehin alle Eigenschaften.

**Nebenbei aufgefallen:** die Dateisenke schreibt in der Produktion nach
`/app/logs/<dienst>-.log`. Unser Einstiegspunkt wechselt vorher in
`/app/$SERVICE_DIR` (ADR-0028), also landen die Dateien unter
`/app/consent/logs/`. Nicht falsch, aber nicht dort, wo Girder es meint — ein
absoluter Pfad wäre eindeutiger als ein relativer.

## Stand

**Behoben in Girder 4.3.0, nachgemessen am 09.09.2026 auf 4.4.0.**

Die Entwicklungsvorlage trägt jetzt `[{CorrelationId}]`. Gemessen am laufenden
`docker compose`-Stapel, in beiden Stufen — die zweite ist die, die zählt:

**Ein Dienst.** `POST /consent/check` mit `X-Correlation-ID: mess-c1-eins`:

```
workertransfer-consent | [16:14:05 INF] [mess-c1-eins] Serilog…RequestLoggingMiddleware:
                          HTTP POST /consent/check responded 401 in 28.5419 ms
workertransfer-consent | [16:14:05 WRN] [mess-c1-eins] Girder…TelemetryMiddleware:
                          HTTP POST /consent/check responded 401 in 35.81ms
```

**Über einen Dienstsprung.** `POST /applications/drafts` mit
`X-Correlation-ID: mess-c1-sprung` — applications-service fragt jobs-service
nach der Stelle:

| Dienst | Treffer auf stdout |
|---|---|
| applications-service | 6 |
| jobs-service | **7** |

Die Kennung überlebt also einen echten HTTP-Sprung und steht in **beiden**
Protokollen. Damit ist der Faden wieder durchgehend — genau das, was hier
gefehlt hat.

**Nebenbei bestätigt:** Serilog lässt eine fehlende Eigenschaft leer. Zeilen
ausserhalb einer Anfrage tragen `[]` statt eines Platzhalters:

```
workertransfer-consent | [12:21:44 INF] [] WorkerTransfer.Consent.Api:
                          Girder für consent-service: 19 Module in Betrieb, 6 ausgelassen
```

Der zweite Punkt des Tickets — die Dateisenke schreibt wegen ADR-0028 nach
`/app/$SERVICE_DIR/logs/` statt nach `/app/logs/` — ist **nicht** Teil dieser
Behebung und auch nicht nachgemessen worden. Er kostet nichts, solange niemand
die Datei im Behälter sucht; wer sie sucht, findet sie einen Ordner tiefer.

Gemeldet 03.09.2026, geschlossen 09.09.2026.
