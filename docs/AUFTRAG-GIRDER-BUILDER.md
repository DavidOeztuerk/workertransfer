# Auftrag an Girder: die fehlende Mitte

Girder hat heute zwei Wege, sich zu registrieren, und beide sind falsch für
jemanden, der weiß, was er tut.

```csharp
AddSharedInfrastructure(config, env, name)          // 13 Module, Girder entscheidet
AddSharedInfrastructure(config, env, name, lambda)  // du schreibst alles selbst
```

Der erste nimmt dem Anwender die Entscheidung. Der zweite **ersetzt** die
Vorgabe, statt sie zu ergänzen — wer ihn nimmt, verliert stillschweigend
dreizehn Module *und* alles, was in der ersten Überladung außerhalb des
Modulsystems steht (Serilog, Swagger, CORS). Genau das ist in WorkerTransfer
passiert: sieben Module statt achtzehn, und niemand hat es entschieden.

**Es fehlt die Mitte: eine sichtbare Vorgabe, von der man begründet abweicht.**

---

## Das Vorbild: Entity Framework Core

EF löst dieselbe Aufgabe seit Jahren, und zwar so:

### Pakete sind nach Verantwortung geschnitten, nicht nach Thema

| Paket | Rolle |
|---|---|
| `Microsoft.EntityFrameworkCore.Abstractions` | Attribute und Schnittstellen, **null Abhängigkeiten** — die Domäne darf es referenzieren |
| `Microsoft.EntityFrameworkCore` | Laufzeit, `DbContext`, Änderungsverfolgung |
| `…​.Relational` | was sich alle SQL-Anbieter teilen |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **ein** Anbieter, von Dritten geschrieben |
| `…​.Design` | nur Entwurfszeit, mit `PrivateAssets="all"` — reicht nicht an Konsumenten weiter |
| `…​.InMemory` | Prüfstand |

Zwei Lehren daraus: **der Kern tut allein nichts** — ohne Anbieter läuft EF
nicht, und das ist Absicht, weil die Wahl dem Anwender gehört. Und
**Entwurfszeit-Werkzeug wird nicht weitergereicht**; ein Dienst, der Girder
benutzt, soll dessen Migrations-Werkzeug nicht in seiner Abhängigkeitskette
haben.

### Die Registrierung ist ein Baumeister, und Anbieter erweitern ihn von außen

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql
               .MigrationsAssembly("MyApp.Migrations")
               .EnableRetryOnFailure(3))
           .EnableSensitiveDataLogging(env.IsDevelopment())
           .UseSnakeCaseNamingConvention());
```

Vier Eigenschaften, die alle tragen:

1. **`UseNpgsql` gehört nicht EF.** Es ist eine Erweiterungsmethode auf
   `DbContextOptionsBuilder`, geliefert vom Anbieterpaket. EF muss von Npgsql
   nichts wissen. **Jeder kann Girder erweitern, ohne dass Girder ihn kennt.**
2. **Verschachtelte Baumeister.** Der äußere ist allgemein, der innere gehört dem
   Anbieter. Man sieht auf einen Blick, welche Einstellung wem gehört.
3. **Vorgaben sind echt, aber sichtbar abschaltbar.**
   `EnableSensitiveDataLogging` ist aus; man schaltet es ein und sieht es dabei.
   Nichts Gefährliches passiert ungefragt, nichts Nützliches fehlt stillschweigend.
4. **Konvention, überschreibbar.** `ApplyConfigurationsFromAssembly` findet
   `IEntityTypeConfiguration<T>` selbst; wer eine Entität anders will, schreibt
   eine Klasse und EF nimmt sie.

**Und was EF *nicht* tut:** es gibt kein `AddEntityFrameworkWithEverything()`.
Der Anwender wählt den Anbieter — immer. Aber er wählt nicht dreißig
Einzelheiten, denn alles andere hat eine Vorgabe.

---

## Was Girder daraus bauen soll

### Die Zielform

```csharp
builder.Services.AddGirder(configuration, environment, "identity-service", girder => girder
    .UseDefaults()                                              // die sichere 13, als ein sichtbarer Aufruf
    .Without(GirderModule.Caching,       "ADR-0013: Einwilligung darf nicht zwischengespeichert werden")
    .Without(GirderModule.Communication, "kein Broker, und der Manager wirft Statuscodes weg")
    .Use(GirderModule.Authorization)
    .UseRateLimiting(rate => rate
        .TrustNoForwardedHeaders()                              // Vorgabe, hier nur sichtbar gemacht
        .PerOrigin())
    .UseJwt(jwt => jwt.VerifyOnly(publicKey, keyId)));
```

Fünf Anforderungen, jede aus einem Schaden abgeleitet, den es wirklich gab:

1. **`UseDefaults()` ist ein Aufruf, den man sieht und löschen kann.** Damit hat
   der Composition Root wieder eine Aussage. Wer ihn wegnimmt, bekommt nichts —
   wie EF ohne Anbieter.
2. **`Without(…)` verlangt eine Begründung als Zeichenkette.** Nicht Zierde: der
   Grund landet dort, wo die Entscheidung steht, statt in einem Dokument, das
   niemand liest. Ohne Begründung übersetzt es nicht.
3. **`Use(…)` ergänzt, `Without(…)` entfernt** — beides nach `UseDefaults()`, in
   beliebiger Reihenfolge, und die letzte Nennung gewinnt.
4. **Modul-Baumeister für alles, was Einstellungen hat**, wie EFs
   `UseNpgsql(conn, npgsql => …)`. Keine Optionsklasse, die man über
   `IConfiguration` erraten muss.
5. **Erweiterbar von außen.** Ein `GirderBuilder` mit öffentlichen
   Erweiterungspunkten, damit `Girder.Redis` oder ein Fremdpaket ein Modul
   beisteuern kann, ohne dass Girder es kennt.

### Die Paketaufteilung dazu

Wie bei EF nach Verantwortung, nicht nach Thema:

- `Girder.Abstractions` bleibt, was es ist: **null Abhängigkeiten**, die Domäne
  darf es referenzieren.
- Was heute in `Girder.Infrastructure` liegt und nur zur Entwurfszeit gebraucht
  wird, wandert oder bekommt `PrivateAssets="all"`.
- Anbieter bleiben getrennt (`Girder.Redis`, `Girder.InMemory`,
  `Girder.Passwords.*`) und bringen ihre `Use…`-Erweiterung selbst mit.

---

## Was gleichzeitig zu beheben ist

Diese Befunde stammen aus dem Einsatz und sind gemessen. Sie gehören in
dieselbe Fassung, weil die neue Form sie sonst zementiert.

1. **Ratenbegrenzung glaubt `X-Forwarded-For` und `X-Real-IP` bedingungslos.**
   Alle drei Wege, und in ganz Girder gibt es keine Vertrauensliste — null
   Treffer auf `KnownProxies`, `KnownNetworks`, `ForwardedHeaders`. Eine Bremse,
   die einem Kopf glaubt, den der Aufrufer setzt, ist keine Bremse. **Das ist
   eine Lücke, kein Mangel.** Vorgabe muss „nicht vertrauen" sein, und
   Vertrauen nur gegen eine erklärte Liste.
2. **Drei Ratenbegrenzer, zwei bremsen nicht, der funktionierende ist nirgends
   verdrahtet.** Einer bleibt.
3. **`ConfigureRateLimitRules` tötet den Prozess beim Start** — die Fabrik löst
   beim Registrieren `IRateLimitService` auf, sich selbst. Kein Protokoll.
4. **`ClientIdStrategy` und `CustomClientIdExtractor` werden nirgends gelesen.**
   Wer „je Herkunft" einstellt, zählt je Benutzer. Girders eigene Tests prüfen
   nur die Vorgabewerte und sehen deshalb grün aus.
5. **`ServiceCommunicationManager` bildet jedes Nicht-2xx auf `null` ab**, und
   `ResilientHttpPolicyHandler` macht daraus eine Ausnahme mit drei Versuchen —
   ein `404` wird dreimal nachgefragt. Ein Statuscode ist eine **Antwort**, keine
   Störung. Der Aufrufer muss ihn bekommen.
6. **Die Korrelationskennung reist nur über den Manager oder MassTransit.** Wer
   `HttpClient` direkt benutzt — der Normalfall — bekommt nichts. Ein
   `DelegatingHandler` über `ConfigureHttpClientDefaults` löst es für alle, etwa
   dreißig Zeilen. Dazu **Baggage setzen statt nur ein Tag**: `LoggingBehavior`
   liest bereits `Activity.Current?.GetBaggageItem("CorrelationId")`, und
   `AddBaggage` kommt in ganz Girder nicht vor — ein Leser ohne Schreiber.

---

## Anwenderdokumentation

Die neue Form ist nur so gut wie das, was jemand darüber lesen kann. Verlangt
wird:

- **Ein Schnellstart**, der einen leeren Dienst in fünfzehn Zeilen zum Laufen
  bringt, und in dem `UseDefaults()` vorkommt.
- **Eine Tabelle aller Module:** was es tut, was es voraussetzt, was es
  registriert, und **ob es in `UseDefaults()` steckt**.
- **Ein Abschnitt „Wann du abweichen willst"** mit echten Beispielen: kein
  Broker, keine Zwischenspeicherung aus rechtlichen Gründen, eigene Bremse.
- **Migrationsanleitung von 3.x**, mit einer Gegenüberstellung alt/neu und der
  ausdrücklichen Warnung, dass das alte Lambda die Vorgabe *ersetzt* hat.
- **Ein Abschnitt zu Souveränität:** was Girder nach außen ruft, wie
  `EgressPolicy` das begrenzt, und wie man den `SovereigntyReport` liest.
- ~~Jede öffentliche Methode mit XML-Doku~~ — **zurückgenommen.** Girder hat dazu
  längst eine Position, und sie steht in `Directory.Build.props`: CS1591 ist
  unterdrückt, weil *„requiring one on every member produces noise, not
  documentation"*. Die Anforderung wurde geschrieben, ohne das nachzusehen. Was
  bleibt: **die Erweiterungsmethoden und Baumeister** dokumentieren, was passiert,
  wenn man sie *nicht* ruft — das ist die Frage, die man beim Verdrahten wirklich
  hat, und sie stellt sich nicht bei einer Fehlercode-Konstante.

## Abnahme

- Ein Dienst kommt mit `UseDefaults()` hoch, ohne weitere Zeile
- `Without(modul, grund)` ohne Grund übersetzt nicht
- Ein Fremdpaket kann ein Modul beisteuern, ohne dass Girder es kennt
- Die Bremse vertraut keinem Kopf, den der Aufrufer setzt — Test dazu
- Ein Statuscode kommt beim Aufrufer an, `404` wird nicht wiederholt — Test dazu
- Eine Korrelationskennung überlebt einen HTTP-Sprung mit blankem `HttpClient` —
  Test dazu
- Die Dokumentation reicht, um einen Dienst ohne Rückfrage aufzusetzen
