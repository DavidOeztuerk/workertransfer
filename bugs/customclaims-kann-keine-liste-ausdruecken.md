# `UserClaims.CustomClaims` kann keinen listenwertigen Anspruch ausdrücken

- **Girder-Fassung:** 2.3.0
- **Gefunden beim:** identity-service, Schritt 1 (Tokenform)
- **Art:** Lücke
- **Blockiert:** nein — die betroffenen Ansprüche fallen aus dem Übergangstoken

## Was passiert

`CustomClaims` ist ein `Dictionary<string, string>`. Ein Anspruch, den ein
Empfänger als JSON-Array erwartet, lässt sich darüber nicht schreiben: aus
`["user", "admin"]` wird entweder eine zusammengefügte Zeichenkette oder gar
nichts.

Girders eigene Ansprüche haben das Problem nicht — Rollen und Berechtigungen
gehen als wiederholte `Claim`-Einträge in die Liste, und wiederholte Ansprüche
serialisiert `JwtSecurityToken` als Array. Genau dieser Weg steht `CustomClaims`
nicht offen: ein Wörterbuch hat je Schlüssel einen Wert.

## Warum es Girders ist

Nur Girder, kein WorkerTransfer-Code (Hilfsmethoden wie im Nachbarticket):

```csharp
[Fact]
public async Task CustomClaims_erzeugt_eine_Zeichenkette_wo_eine_Liste_gebraucht_wird()
{
    var token = await Service().GenerateTokenAsync(new UserClaims
    {
        UserId = Guid.NewGuid().ToString(),
        Email = "anna@example.com",
        CustomClaims = new() { ["roles"] = string.Join(",", new[] { "user", "admin" }) }
    });

    var roles = Payload(token.AccessToken).GetProperty("roles");

    roles.ValueKind.Should().Be(JsonValueKind.String);
    roles.ValueKind.Should().NotBe(JsonValueKind.Array);
    roles.GetString().Should().Be("user,admin");
}
```

Besteht gegen 2.3.0.

Zugesagt ist auch hier nichts — `CustomClaims` sagt nirgends, dass es beliebige
JSON-Formen tragen könne. Die Lücke ist, dass eine Migration genau daran hängt:
den einen Anspruch nachzubilden, den ein bestehender Verbraucher in einer
bestimmten Form erwartet, ist der Zweck eines freien Anspruchsfeldes.

Ein `Dictionary<string, string[]>` daneben, oder ein `IReadOnlyList<Claim>`, das
`GenerateAccessTokenAsync` unverändert anhängt, würde es schließen.

## Was es uns kostet

Hier nichts, und das ist eine Messung und kein Glück: `roles` und `permissions`
fallen aus dem Übergangstoken heraus.

Der tragende Grund steht in `CLAUDE.md` — die Rollenprüfungen lesen aus
`user_tenant_memberships`, nie aus dem Token („der Token sagt, für *welches*
Unternehmen jemand handelt, nie mit welchem Recht"). Der Anspruch war also nie
maßgeblich. Dazu die Messung: fehlt `roles`, setzt Pythons `TokenPayload` `[]`
und nimmt den Token an; steht dort eine Zeichenkette statt einer Liste, lehnt
pydantic ihn ab. Die einzigen Leser sind `/auth/session` und `/me`, und beide
können die Rollen aus `users.roles` beantworten, wo sie ohnehin stehen.

Sollte später ein listenwertiger Anspruch wirklich gebraucht werden, bliebe nur,
den Zugriffstoken an `IJwtService` vorbei selbst zu bauen — und damit auf das
Stück zu verzichten, dessentwegen wir Girder nehmen.

## Stand

- [ ] gemeldet
- [ ] in Girder behoben, Fassung: <…>
- [ ] Umweg hier entfernt
