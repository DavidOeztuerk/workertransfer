# `IJwtService` kann keinen Token ohne `aud` ausstellen

- **Girder-Fassung:** 2.3.0
- **Gefunden beim:** identity-service, Schritt 1 (Tokenform)
- **Art:** Lücke
- **Blockiert:** nein — die Gegenseite ist unser Code, siehe „Was es uns kostet"

## Was passiert

`JwtService.GenerateTokenAsync` setzt `aud` bei jedem Token, und es gibt keinen
Weg daran vorbei: die Zielgruppe geht fest in den `JwtSecurityToken`, und eine
leere Zielgruppe lehnt `ValidateJwtSettings()` schon im Konstruktor ab.

Für einen Empfänger, der keine Zielgruppen-Politik hat, ist ein `aud` kein
neutraler Zusatz, sondern ein Ablehnungsgrund. RFC 7519 §4.1.3 verlangt, dass
ein Empfänger einen Token zurückweist, wenn er sich in `aud` nicht wiederfindet
— PyJWT tut genau das:

```
InvalidAudienceError - Invalid audience
```

Damit kann ein Girder-Dienst keinen Token ausstellen, den ein bestehender
Verbraucher ohne eigene Zielgruppen-Erwartung liest. Das ist die Lage in jeder
Migration, in der der neue Aussteller vor den Verbrauchern umgestellt wird.

## Warum es Girders ist

Nur Girder, kein WorkerTransfer-Code:

```csharp
private const string Secret = "test-secret-with-at-least-thirty-two-bytes-xx";

private static JwtService Service(string audience = "workertransfer")
{
    var settings = new JwtSettings
    {
        Secret = Secret,
        Issuer = "workertransfer-identity",
        Audience = audience,
        ExpireMinutes = 15
    };
    var shared = SigningKey.FromSharedSecret(Secret, kid: null);
    return new JwtService(
        Options.Create(settings),
        new KeyRing([shared], shared),
        NullLogger<JwtService>.Instance);
}

private static JsonElement Payload(string jwt)
{
    var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
    var payload = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
    return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;
}

[Fact]
public async Task Jeder_ausgestellte_Token_traegt_aud()
{
    var token = await Service().GenerateTokenAsync(new UserClaims
    {
        UserId = Guid.NewGuid().ToString(),
        Email = "anna@example.com"
    });

    Payload(token.AccessToken).TryGetProperty("aud", out var aud).Should().BeTrue();
    aud.GetString().Should().Be("workertransfer");
}

[Fact]
public void Und_eine_leere_Zielgruppe_ist_kein_Ausweg()
{
    var leer = () => Service(audience: "");

    leer.Should().Throw<InvalidOperationException>()
        .WithMessage("*Audience is required*");
}
```

Beide bestehen gegen 2.3.0 — das heißt hier: das Verhalten ist da.

Zugesagt ist es nirgends, deshalb Lücke und nicht Fehler. Der Berührungspunkt
ist der Migrationsabschnitt der README, der ausdrücklich den Fall führt, in dem
alter und neuer Aussteller gleichzeitig leben:

> „Both together is the shape a migration has, where the old and the new issuer
> are live at once."

Das ist über das *Prüfen* gesagt und gilt dort auch. Für das *Ausstellen* gibt
es die Entsprechung nicht: ein Token, den der alte Bestand lesen kann, lässt
sich nicht erzeugen.

## Was es uns kostet

Nichts, was uns aufhält — aber die Gegenseite muss es tragen. `worker_auth`
entschlüsselt ab jetzt mit `options={"verify_aud": False}`, damit der Anspruch
ignoriert statt zum Ablehnungsgrund wird. Gemessen samt Gegenproben: mit und
ohne `aud` grün, falsche Signatur weiterhin `InvalidSignatureError`, abgelaufen
weiterhin `ExpiredSignatureError`.

Das ist keine Verschlechterung — Python hatte nie eine Zielgruppen-Erwartung —,
aber es ist eine Schuld: solange die Zeile steht, prüft Python die Zielgruppe
nicht, obwohl inzwischen eine im Token steht. Sie wird in
`docs/uebergang-python-dotnet.md` geführt und wird zu `audience=…`, sobald keine
alten Token mehr umlaufen.

Ein `Audience`, das leer bleiben darf und dann keinen Anspruch schreibt, würde
die Schuld überflüssig machen.

## Stand — kein Auftrag

- [x] gemeldet
- [ ] ~~in Girder behoben~~ — **wird nicht behoben, und das ist richtig so**
- [ ] erledigt sich mit Ü-1, wenn Python geht (Schritt 12)

Dass Girder bei jedem Token `aud` setzt, ist kein Mangel, sondern korrektes
Verhalten. Es stört einzig an der Naht zu PyJWT, das einen Token mit `aud`
ablehnt, wenn keine Zielgruppe erwartet wird. Die Gegenseite trägt es mit
`options={"verify_aud": False}` in `worker_auth` — das ist Ü-1 in
`docs/uebergang-python-dotnet.md`, also Gerüst mit Verfallsdatum.

Hier ist nichts zu beheben, nur etwas zu löschen, und das passiert in Schritt 12.
