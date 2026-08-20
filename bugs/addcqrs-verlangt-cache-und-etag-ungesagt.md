# `AddCQRS` verlangt einen Cache und einen ETag-Erzeuger, ohne es zu sagen

- **Girder-Fassung:** 2.3.0
- **Gefunden beim:** identity-service, Schritt 2 (Mediator-Entscheidung)
- **Art:** Lücke
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

Die Abhilfe wäre klein: die beiden Abhängigkeiten mit einem Vorgabewert
versehen (`IDistributedCacheService? cache = null`), oder `AddCQRS` prüft beim
Start und nennt die fehlenden Aufrufe, so wie die Module es tun.

## Stand

- [ ] gemeldet
- [ ] in Girder behoben, Fassung: <…>
- [ ] Umweg hier entfernt
