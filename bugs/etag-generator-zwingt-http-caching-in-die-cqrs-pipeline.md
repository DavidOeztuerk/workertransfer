# `IETagGenerator` zwingt HTTP-Antwort-Caching in die CQRS-Pipeline

- **Girder-Fassung:** 2.3.0
- **Gefunden beim:** identity-service, Schritt 2 — beim Aufarbeiten von
  [`addcqrs-verlangt-cache-und-etag-ungesagt.md`](addcqrs-verlangt-cache-und-etag-ungesagt.md)
- **Art:** Fehler — Schichtverstoß, nicht bloß Geschmack
- **Blockiert:** nein

## Was passiert

`CacheInvalidationBehavior` liegt in `Girder.Application` und verlangt
`IETagGenerator`. Diese Schnittstelle ist HTTP:

- Ihre XML-Doku sagt „for HTTP responses".
- Ihre Muster sind API-Pfade — das Beispiel in der Doku ist `"/api/jobs*"`.
- Ihre einzige Umsetzung liegt in `Girder.Infrastructure/Caching/Http/`.
- Sie wird an **genau zwei Stellen** registriert, beide in
  `HttpCachingServiceCollectionExtensions` (Zeile 35 und 70) — also einzig durch
  `AddHttpResponseCaching(...)`.

Damit gilt: **wer Girders CQRS-Pipeline benutzen will, muss HTTP-Antwort-Caching
registrieren.** Auch ein Dienst, der kein HTTP-Antwort-Caching hat und keins
will.

### Die Schnittstelle ist drei Dinge auf einmal

Neun Methoden, drei Zuständigkeiten:

| | Methoden | Art |
|---|---|---|
| Erzeugen | `GenerateETag` ×4 | rein, zustandslos |
| Prüfen | `ValidateETag` | rein, zustandslos |
| **Speichern** | `StoreETagAsync`, `GetCachedETagAsync`, `InvalidateETagAsync`, `InvalidateETagsByPatternAsync` | zustandsbehaftet, über einen Cache |

Der Name nennt nur das Erste, die Zusammenfassung nur die ersten beiden. Vier von
neun Methoden sind Speicherzugriffe und stehen in keiner der beiden Beschreibungen.

**Die Pipeline benutzt genau eine davon:** `InvalidateETagsByPatternAsync`. Sie
erzeugt nichts, prüft nichts, speichert nichts und liest nichts.

## Warum es Girders ist

Es braucht keinen Testfall — der Verstoß ist statisch und mit `grep` belegbar.
Kein WorkerTransfer-Code ist beteiligt:

```
src/Girder.Application/Behaviors/CacheInvalidationBehavior.cs
    -> Girder.Application.Abstractions.IETagGenerator
       einzige Umsetzung:   src/Girder.Infrastructure/Caching/Http/ETagGenerator.cs
       einzige Registrierung: HttpCachingServiceCollectionExtensions:35,70
                              (= AddHttpResponseCaching)
```

Eine transportunabhängige Schicht hängt an einer Abstraktion, die sich selbst als
HTTP beschreibt und nur von einem HTTP-Modul bereitgestellt wird.

## Was es uns kostet

Es ist die Ursache der zweiten Ausnahme im Schwesterticket. Dort ist das Symptom
beschrieben — eine rohe DI-Ausnahme über einen Typ, den der Leser nie
geschrieben hat. Hier steht, warum sie überhaupt entstehen konnte.

Konkret: `jobs-service` wird Commands mit `ETagInvalidationPatterns` haben. Damit
die wirken, müsste er `AddHttpResponseCaching` registrieren — nicht weil er
HTTP-Antwort-Caching will, sondern weil sonst der Invalidierer fehlt. Der
Composition Root sagte dann etwas aus, was nicht gemeint ist. Dieselbe Klasse von
Schaden wie beim Schwesterticket, andere Ursache.

## Die Behebung: die Abhängigkeit fällt weg, sie wird nicht aufgeteilt

Die naheliegende Lösung wäre, `IETagGenerator` zu teilen — den reinen Teil
behalten, die vier Speichermethoden in ein `IETagStore` nach
`Girder.Abstractions.Caching` heben, unabhängig von HTTP registrierbar.

Das wäre unnötig. Gemessen, `ETagGenerator.cs:168`:

```csharp
public async Task InvalidateETagsByPatternAsync(string pattern, CancellationToken cancellationToken = default)
{
    if (_cacheService == null) { … return; }
    var fullPattern = $"{ETagCachePrefix}{pattern}";      // ETagCachePrefix = "etag:"
    await _cacheService.RemoveByPatternAsync(fullPattern, cancellationToken);
}
```

Es ist **ein Aufruf auf `IDistributedCacheService`** — den das Behavior bereits
hält. Die gesamte Abhängigkeit auf `IETagGenerator` verbirgt eine
Zeichenkettenverkettung.

Also: das Behavior ruft direkt

```csharp
await _cacheService.RemoveByPatternAsync(
    $"{CacheKeys.ETagPrefix}{processedPattern}", cancellationToken);
```

und `IETagGenerator` verschwindet aus `Girder.Application`.

**Der ehrliche Preis:** geteilt wird dann das Präfix statt einer Schnittstelle.
`ETagCachePrefix` ist heute `private const` in `ETagGenerator` und muss eine
benannte Konstante an einer Stelle werden, die beide sehen. Das ist besser als
der jetzige Zustand — die Übereinkunft über den Schlüssel besteht ohnehin schon,
sie ist nur in einem Kommentar versteckt statt benannt.

## Auftrag für die Girder-Sitzung

Diese Datei ist die Übergabe. Sie liegt in WorkerTransfer; eine Girder-Sitzung
sieht sie nicht von selbst.

**Reihenfolge:** erst das Schwesterticket
([`addcqrs-verlangt-cache-und-etag-ungesagt.md`](addcqrs-verlangt-cache-und-etag-ungesagt.md)),
dann dieses. Danach muss die dortige bedingte Anforderung nur noch **eine** Sache
verlangen — den Cache — statt zwei.

**Zu ändern:**

1. `ETagCachePrefix` wird eine benannte, sichtbare Konstante (Vorschlag:
   `Girder.Abstractions.Caching`), `ETagGenerator` benutzt sie weiter.
2. `CacheInvalidationBehavior` invalidiert ETags direkt über
   `IDistributedCacheService` und verliert den Parameter `IETagGenerator?`.
3. `Girder.Application` hat danach keinen Verweis mehr auf `IETagGenerator`.

**Nicht anfassen:**

- **`IETagGenerator` selbst bleibt.** `ResponseCachingHeaderMiddleware` ist ihr
  rechtmäßiger Konsument; dieses Ticket löscht sie nicht, es nimmt sie nur aus
  der Anwendungsschicht.
- Die Aufteilung in `IETagStore`. Sie ist bei einem Einzeiler Zeremonie.
- Das Verhalten der Invalidierung. Präfix, Muster und Ersetzung der Platzhalter
  bleiben, wie sie sind — der Aufrufer wechselt, nicht die Wirkung.

**Regressionstests**, dauerhaft in Girders Suite:

- Eine Assembly mit einem `ICacheInvalidatingCommand`, der
  `ETagInvalidationPatterns` deklariert, läuft **ohne** `AddHttpResponseCaching`.
- Die Invalidierung entfernt wirklich die Schlüssel mit `etag:`-Präfix — gegen
  den Cache geprüft, nicht gegen einen Mock-Aufruf. Ein Mock bewiese nur, dass
  eine Methode gerufen wurde, und genau die Frage ist hier, mit welchem
  Schlüssel.
- Gegenprobe: Präfix entfernen, der Test muss fallen. Achte darauf, dass der
  Bruch **übersetzt** — ein Build-Fehler sieht in der Ausgabe aus wie ein
  bestandener Test.

**Ein Nebenbefund, ungeprüft:** `ETagGenerator` nimmt selbst
`IDistributedCacheService?` **ohne** Vorgabewert. Das ist dieselbe Form wie im
Schwesterticket. Ob es dort auch zuschlägt, hängt davon ab, ob
`AddHttpResponseCaching` einen Cache mitregistriert — das ist nicht nachgesehen
worden. Prüfen, wenn die Sitzung ohnehin in der Datei steht.

## Stand — erledigt

- [x] gemeldet
- [x] in Girder behoben, Fassung: **3.0.0**
- [x] Schwesterticket erledigt sich mit: die Anforderung nennt nur noch den Cache

`CacheInvalidationBehavior` verwirft ETags direkt über `IDistributedCacheService`;
`CacheKeys.ETagPrefix` liegt in `Girder.Abstractions.Caching`. `IETagGenerator`
ist nach `Girder.Infrastructure.Caching.Http` gezogen, neben ihre Umsetzung —
`Girder.Application` *kann* sie nicht mehr erreichen.

**Der Nebenbefund war echt und ist mit behoben.** `ETagGenerator` nimmt
`IDistributedCacheService` jetzt verpflichtend, die vier unerreichbaren
Degradationszweige sind weg, und beide `AddHttpResponseCaching`-Überladungen
melden die Anforderung an.

**Ein zweiter Fehler fiel dabei auf, weil der Test gegen den Cache lief statt
gegen einen Mock:** `Girder.InMemory.RemoveByPatternAsync` verglich das rohe
Muster mit Schlüsseln, die das Präfix des Speichers tragen. `AddInMemoryCache`
setzt immer eines — also traf **jedes** `InvalidationPatterns` und jedes
`ETagInvalidationPatterns` in einer In-Memory-Installation nichts und löschte
lautlos nichts. Redis hat sein Muster immer präfigiert; die beiden waren sich
über denselben Vertrag uneinig. Ein Mock-Test wäre dafür blind gewesen.
