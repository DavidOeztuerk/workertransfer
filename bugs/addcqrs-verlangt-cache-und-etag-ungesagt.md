# `AddCQRS` verlangt einen Cache und einen ETag-Erzeuger, ohne es zu sagen

- **Girder-Fassung:** 2.3.0
- **Gefunden beim:** identity-service, Schritt 2 (Mediator-Entscheidung)
- **Art:** Fehler — beide Behaviors prüfen im Rumpf auf `null`, die Absicht ist also
  eindeutig „optional"; der Container hält sich nicht daran
- **Blockiert:** nein — zwei Zeilen Abhilfe, siehe unten

## Was passiert

`AddCQRS(assembly)` registriert sechs Pipeline-Behaviors fest. Zwei davon
brauchen Dienste, die `AddCQRS` selbst nicht registriert und auch nirgends
nennt. Der erste Aufruf über den Mediator scheitert mit einer rohen
DI-Ausnahme:

```
System.InvalidOperationException : Unable to resolve service for type
'Girder.Abstractions.Caching.IDistributedCacheService' while attempting to
activate 'Girder.Application.Behaviors.CacheInvalidationBehavior`2[…]'.
```

Nach `AddInMemoryCache(prefix)` folgt die zweite:

```
System.InvalidOperationException : Unable to resolve service for type
'Girder.Application.Abstractions.IETagGenerator' while attempting to
activate 'Girder.Application.Behaviors.CacheInvalidationBehavior`2[…]'.
```

Zwei Dinge machen das schwer zu erraten:

- **Der Code sieht aus, als wäre es optional.** Beide Behaviors nehmen
  `IDistributedCacheService?` — mit Fragezeichen. Die .NET-Container-Auflösung
  kennt C#-Nullability nicht; ohne Vorgabewert wird daraus kein `null`, sondern
  eine Ausnahme. Die Zusage im Typ gilt also nicht.
- **`IETagGenerator` ist ein HTTP-Begriff.** Registriert wird er einzig von
  `AddHttpResponseCaching(...)`. Wer Girders CQRS-Pipeline benutzen will, muss
  damit das HTTP-Antwort-Caching mitregistrieren — für ein Behavior, das über
  Commands läuft und mit HTTP nichts zu tun hat.

## Warum es Girders ist

Nur Girder, kein WorkerTransfer-Code:

```csharp
public sealed record Ping(string Wort) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Wort);
}

[Fact]
public async Task Kommt_der_Mediator_ohne_registrierten_Cache_hoch()
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddDebug());
    services.AddCQRS(typeof(Ping).Assembly);

    var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

    // wirft: Unable to resolve service for type 'IDistributedCacheService'
    await mediator.Send(new Ping("hallo"));
}
```

Mit beiden Registrierungen läuft derselbe Testfall durch — gemessen, die
Antwort war `hallo`:

```csharp
services.AddInMemoryCache("messung");                              // Girder.InMemory.Caching
services.AddSingleton<IETagGenerator, ETagGenerator>();            // Girder.Application.Abstractions
services.AddCQRS(typeof(Ping).Assembly);
```

Zugesagt ist das Verhalten nirgends, deshalb Lücke und nicht Fehler. Der
Berührungspunkt ist die Zusage, die Girders README für die Module der
`AddSharedInfrastructure`-Kette macht — und die genau diesen Fall beschreibt:

> „Where a module genuinely needs something it cannot provide — a cache needs a
> cache server — it says so **at startup**, naming the call that fixes it"

und weiter zur Pipeline-Seite:

> „throws there rather than on the first request that happens to reach the
> middleware — in production, naming a Girder-internal type the reader never
> wrote."

`AddCQRS` steht außerhalb dieser Kette und tut beides nicht: es scheitert bei
der ersten Anfrage, und es nennt einen Girder-internen Typ, den der Leser nie
geschrieben hat.

## Was es uns kostet

Zwei Zeilen je Dienst, und eine Aussage, die in einem Dienst falsch klingt.

Für identity-service ist es hinnehmbar: nichts wird zwischengespeichert, weil
keine Query `ICacheableQuery` implementiert, und der Cache steht ungenutzt da.

Unangenehm wird es bei **consent-service**. ADR-0013 sagt, eine Einwilligung
werde synchron gelesen und **nie** zwischengespeichert; ein Widerruf muss beim
nächsten Lesen wirken. Dort stünde dann `AddInMemoryCache(...)` im Composition
Root — funktional folgenlos, aber der Composition Root ist die Stelle, der man
ansehen soll, welche Querschnittsentscheidungen ein Dienst trifft. Er sagte dann
das Gegenteil dessen, was gilt.

## Die Behebung

Zwei naheliegende Wege wurden geprüft und beide verworfen.

**`= null` als Vorgabewert.** Der Container ehrt Vorgabewerte — isoliert
nachgemessen, nur `Microsoft.Extensions.DependencyInjection`:

```
mit Vorgabewert   : OK, Dep = null
ohne Vorgabewert  : InvalidOperationException
```

Es würde also funktionieren, und es ist trotzdem falsch: es macht den
gefährlichsten Fall stumm. Wer eine Query mit `ICacheableQuery` markiert und
keinen Cache registriert hat, bekommt keinen Fehler, keinen Logeintrag — nur
keine Zwischenspeicherung. Ein Merkmal, das man anschaltet und das nichts tut,
ist schlimmer als eines, das fehlt.

**Ein Schalter am Aufruf** (`AddCQRS(assembly, useDefaultCache: true)`). Er
stellt die Frage an die Falschen: `consent-service` müsste `false` schreiben und
damit eine Aussage über Caching treffen, obwohl die Frage sich dort nicht
stellt. Das ist die Beschwerde aus „Was es uns kostet", nur verschoben. An der
Aufrufstelle liest sich `AddCQRS(asm, true)` außerdem wie nichts.

### Was stattdessen: die Anforderung gilt bedingt

Girder hat den Mechanismus bereits und verspricht ihn in seiner README:
`RequiresProvider<T>(requiredBy, remedy)` sammelt Anforderungen, ein
`IStartupFilter` (`ProviderRequirementValidator`) prüft sie beim Start und nennt
den Aufruf, der sie erfüllt. `CommunicationModule` tut das für genau diesen Fall
— einen Cache. `AddCQRS` tut es nicht, weil es außerhalb der
`AddSharedInfrastructure`-Kette steht; die Anforderungen liegen aber als
Singleton in der `ServiceCollection`, also kommt es daran.

Der tragende Zusatz: **die Anforderung gilt nur, wenn der Dienst wirklich
zwischenspeichert.** `AddCQRS` bekommt die Assemblies ohnehin und scannt sie
ohnehin.

- Findet es **kein** `ICacheableQuery` und **kein** `ICacheInvalidatingCommand`,
  registriert es die beiden Cache-Behaviors **gar nicht erst**. Kein Cache
  nötig, keine Aussage im Composition Root, kürzere Pipeline. Das ist
  identity-service und consent-service.
- Findet es eines, scheitert der Start und benennt den Verursacher:

  ```
  CreateJobQuery implementiert ICacheableQuery, die CQRS-Pipeline braucht also
  einen Cache. Registriere deinen eigenen (AddCaching / AddRedisConnection) oder
  nimm den eingebauten mit AddInMemoryCache("jobs-service").
  ```

Ein eigener Provider erfüllt die Bedingung, weil auf die **Schnittstelle**
geprüft wird und nicht darauf, wer sie registriert hat.

### Die Folge fürs Typsystem

Werden die Behaviors nur noch dort registriert, wo ein Cache garantiert ist,
dürfen die Parameter aufhören, nullable zu sein: `IDistributedCacheService
cache`, verpflichtend, und die `if (_cache == null)`-Prüfungen in den Rümpfen
fallen weg. Das ist das Gegenteil von `= null` — die Zusage im Typ gilt dann
wirklich.

---

## Auftrag für die Girder-Sitzung

Diese Datei ist die Übergabe. Sie liegt in WorkerTransfer; eine Girder-Sitzung
sieht sie nicht von selbst.

**Zu ändern** in `src/Girder.Application/Extensions/ServiceCollectionExtensions.cs`
und den zwei Behaviors:

1. `AddCQRS` scannt die übergebenen Assemblies auf Implementierer von
   `ICacheableQuery` und `ICacheInvalidatingCommand`.
2. Ohne Treffer: `CachingBehavior` und `CacheInvalidationBehavior` nicht
   registrieren.
3. Mit Treffer: Anforderung auf `IDistributedCacheService` (und
   `IETagGenerator`) anmelden, mit einer Meldung, die den **gefundenen Typ**
   nennt und den Aufruf, der sie erfüllt.
4. Die Parameter der beiden Behaviors werden verpflichtend, die Null-Prüfungen
   in den Rümpfen entfallen.

**Nicht anfassen:**

- Die **Reihenfolge** der sechs Behaviors. Sie ist bewusst gesetzt — der
  Kommentar `// Caching BEFORE Performance measurement` steht im Code.
- `LoggingBehavior`, `ValidationBehavior`, `AuditBehavior`. Sie haben keine
  fehlende Abhängigkeit und stehen hier nicht zur Debatte.
- `AddHttpResponseCaching`. Dass `IETagGenerator` ein HTTP-Begriff in einer
  Command-Pipeline ist, ist ein **eigener** Konstruktionsfehler und steht in
  [`etag-generator-zwingt-http-caching-in-die-cqrs-pipeline.md`](etag-generator-zwingt-http-caching-in-die-cqrs-pipeline.md).
  Erst dieses Ticket, dann jenes — danach verlangt die bedingte Anforderung hier
  nur noch **eine** Sache, den Cache, statt zwei.

**Regressionstests**, dauerhaft in Girders Suite, nicht nur als Beleg hier:

- Der Testfall aus „Warum es Girders ist" wird grün, **ohne** zusätzliche Zeilen
  im Aufrufer.
- Eine Assembly **mit** einer `ICacheableQuery` und ohne Cache scheitert beim
  Start, und die Meldung nennt den Typ.
- Ein selbst registrierter `IDistributedCacheService` erfüllt die Bedingung.
- Gegenprobe: nimm die bedingte Registrierung heraus und sieh nach, dass die
  Tests fallen. Achte darauf, dass der Bruch **übersetzt** — ein Build-Fehler
  sieht in der Ausgabe aus wie ein bestandener Test.

**Danach, und das ist der größere Teil:** Fassung anheben, nach GitHub Packages
veröffentlichen, und in WorkerTransfer `GirderVersion` in
`dotnet/Directory.Packages.props` nachziehen.

**Bauen und Testen in getrennten Aufrufen** — verkettet scheitern die
Testcontainers-Reihen und sehen dabei aus wie echte Testfehler.

## Stand

- [ ] gemeldet
- [ ] in Girder behoben, Fassung: <…>
- [ ] Umweg hier entfernt
