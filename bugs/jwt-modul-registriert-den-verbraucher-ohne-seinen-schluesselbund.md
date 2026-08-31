# Das Jwt-Modul registriert `IJwtService`, aber niemand registriert den `KeyRing`

- **Girder-Fassung:** 4.0.1
- **Gefunden beim:** Umstieg auf `AddGirder(...)` mit `UseDefaults()` (H1)
- **Art:** Fehler — der vorgegebene Weg startet nicht
- **Blockiert:** **ja.** Kein Dienst kommt hoch.

## Was passiert

Der Weg, den Girder 4 vorgibt, bricht beim Bauen des Containers:

```
Unable to resolve service for type 'Girder.Infrastructure.Security.Keys.KeyRing'
while attempting to activate 'Girder.Infrastructure.Security.JwtService'.
```

Reproduziert mit **null** Fremdcode — ein leeres
`Microsoft.NET.Sdk.Web`-Projekt, nur `Girder.Infrastructure` 4.0.1:

```csharp
var bau = WebApplication.CreateBuilder(args);

bau.Services.AddGirder(bau.Configuration, bau.Environment, "probe", girder => girder
    .UseDefaults()
    .UseJwt(jwt => jwt.FromSharedSecret())
    .Without(GirderModule.Authorization, "braucht einen Anbieter, hier nicht noetig"));

bau.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });

bau.Build();   // -> InvalidOperationException
```

`appsettings.json` trägt ein gültiges `JwtSettings:Secret`; am Geheimnis liegt es
nicht.

## Warum es Girders ist

Drei Stellen, die zusammen nicht mehr passen:

**1. Der Katalog registriert den Verbraucher bedingungslos.**
`GirderModuleCatalogue.cs:47`

```csharp
Entry(GirderModule.Jwt, girder =>
{
    girder.Services.AddScoped<IJwtService, JwtService>();
    girder.Services.AddSingleton<ITotpService, TotpService>();
    girder.Services.AddSingleton<IErrorMessageService, ErrorMessageService>();
}),
```

**2. Der Verbraucher verlangt den Schlüsselbund aus dem Container.**
`JwtService.cs:29`

```csharp
public JwtService(
    IOptions<JwtSettings> jwtSettings,
    KeyRing keys,                      // <- nicht nullbar, aus DI
    ILogger<JwtService> logger, …)
```

**3. Registriert wird der Schlüsselbund nur auf dem ALTEN Weg.**
Das einzige `AddSingleton(keys)` in ganz Girder steht in `JwtModule.cs:45` —
also in `InfrastructureBuilder.AddJwtAuthentication(...)`, das der neue
Baumeister nicht ruft:

```csharp
var keys = KeyRingFactory.Build(options, builder.Configuration);

if (keys is not null)
{
    builder.Services.AddSingleton(keys);
    builder.Services.AddScoped<IJwtService, JwtService>();   // NUR wenn es Schluessel gibt
}
```

`UseJwt(...).FromSharedSecret()` geht stattdessen über
`AddJwtAuthentication(configuration, environment)`, das das **Schema** aufsetzt,
und `JwtBuilder.Apply()` steigt danach aus:

```csharp
if (_verify.Count == 0 && _authority is null)
{
    // FromSharedSecret registered the scheme itself.
    return;
}
```

Der Schlüsselbund landet dabei nie im Container.

**Die Wurzel ist die Umkehrung der Bedingung.** Der alte Weg registrierte
`IJwtService` *nur, wenn es Schlüssel gibt*; der Katalog registriert ihn
*immer*. Damit ist ein Verbraucher bedingungslos angemeldet, dessen Abhängigkeit
bedingt ist.

## Warum es schlimmer ist, als es aussieht

Ohne `ValidateOnBuild` fällt es beim Start **nicht** auf. Der Container nimmt die
Registrierung an; erst die erste Auflösung von `IJwtService` wirft — also die
erste Anfrage, die ein Token anfasst. Wer die Prüfung nicht eingeschaltet hat,
findet das in Produktion.

## Was es uns kostet

Alles. Elf Dienste kommen nicht hoch; die vier Reihen ohne Dienstgrundlage
(Gateway, Outbox, Skills, Ganzes) sind grün, alle elf anderen rot.

Ein Umweg wäre möglich — den `KeyRing` selbst bauen und registrieren, oder über
`Use(GirderModule.Jwt, register)` die alte Registrierung mitgeben. **Nicht
gemacht**, solange nicht entschieden ist: ein Umweg um einen Bibliotheksfehler
kostet zweimal, und der zweite Preis fällt an, wenn der Fehler behoben ist und
der Umweg stehen bleibt.

## Vorschlag

`FromSharedSecret()` sollte denselben `KeyRing` registrieren, den es dem Schema
gibt — er wird in `AddJwtAuthentication(configuration, environment)` bereits
gebaut (`ServiceCollectionExtensions.cs:257`):

```csharp
var shared = SigningKey.FromSharedSecret(secret, kid: null);
services.AddJwtAuthentication(new KeyRing([shared], shared), null, configuration, environment);
```

Es fehlt genau ein `services.AddSingleton(keyRing)` auf diesem Pfad. Dieselbe
Frage stellt sich für `VerifyOnly`/`Issue`/`From`: `Apply()` reicht den
`KeyRing` an `AddJwtAuthentication` weiter, aber in den Container legt ihn dort
ebenfalls niemand.

Alternativ: `IJwtService` im Katalog nur registrieren, wenn Schlüssel feststehen
— dann fehlt es ehrlich, statt unauflösbar zu sein.

## Stand

- [ ] gemeldet
- [ ] `UseJwt` legt den `KeyRing` in den Container
- [ ] Umweg hier entfernt (falls einer beschlossen wird)
