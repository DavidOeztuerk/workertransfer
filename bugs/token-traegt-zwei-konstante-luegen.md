# Das Zugriffstoken trägt zwei Ansprüche, die immer dasselbe sagen — und zwei Richtlinien lesen sie

- **Girder-Fassung:** 4.0.2
- **Gefunden beim:** H5, Prüfung von fremder Hand; danach am Quelltext nachgeprüft
- **Art:** Fehler — stiller Rückfall auf Vorgabewerte an einer Sicherheitsgrenze
- **Blockiert:** nein, heute nicht. Wir benutzen die betroffenen Richtlinien
  nicht. Wer sie einschaltet, sperrt entweder alle aus oder lässt Gesperrte durch.

## Was passiert

`JwtService.CreateAccessToken` legt zwei Ansprüche **bedingungslos** ins Token
(`JwtService.cs:117-118`):

```csharp
new("email_verified", user.EmailVerified.ToString(), ClaimValueTypes.Boolean),
new("account_status", user.AccountStatus)
```

Beide Werte kommen aus `UserClaims`, und beide haben dort eine **Vorgabe**
(`UserClaims.cs:14-15`):

```csharp
public bool   EmailVerified { get; set; } = false;
public string AccountStatus { get; set; } = "Active";
```

Wer `UserClaims` befüllt, ohne diese zwei Felder zu setzen — und nichts zwingt
dazu —, bekommt ein Token, das **immer** `email_verified: false` und **immer**
`account_status: "Active"` sagt. Unabhängig davon, was wahr ist.

## Reproduktion, ohne fremden Code

```csharp
var jwt = anbieter.GetRequiredService<IJwtService>();

// Ein Konto, dessen Adresse bestaetigt und das gesperrt ist.
var token = jwt.CreateAccessToken(new UserClaims
{
    UserId = "…",
    Email  = "…",
    // EmailVerified und AccountStatus bewusst NICHT gesetzt —
    // genau so, wie es jeder Aufrufer tut, der sie nicht kennt.
});

// Im Rumpf steht:
//   "email_verified": "False"
//   "account_status": "Active"
```

Gemessen an einem echten Lauf: eine Adresse unmittelbar zuvor über
`/auth/verify-email` bestätigt, danach angemeldet — im Token steht
`email_verified: false`. Und nach einer Kontosperrung steht dort weiterhin
`account_status: "Active"`.

## Warum es mehr ist als ein falsches Feld

Girder liest diese Ansprüche selbst. Zwei Berechtigungsprüfer hängen daran:

- `EmailVerifiedHandler.cs` — lässt nur durch, wessen Adresse bestätigt ist
- `ActiveAccountHandler.cs` — lässt nur durch, wessen Konto aktiv ist

Mit den Vorgabewerten heisst das:

| Richtlinie | Wirkung mit Vorgabe |
|---|---|
| `EmailVerified` | sperrt **jeden** aus, auch wer bestätigt hat |
| `ActiveAccount` | lässt **jeden** durch, auch ein gesperrtes Konto |

Die zweite ist die gefährliche. Sie sieht aus wie eine Prüfung, ist aber eine
Konstante — und ausgerechnet an der Stelle, an der ein gesperrtes oder
gelöschtes Konto aufgehalten werden soll.

## Warum es niemand bemerkt

Weil ein Vorgabewert nicht fehlt, sondern **antwortet**. Ein nicht gesetztes
Feld wäre als `null` sichtbar geworden; `false` und `"Active"` sehen aus wie
Ergebnisse. Und Girders eigene Tests prüfen, dass die Ansprüche *vorhanden*
sind — nicht, dass sie *stimmen*.

## Nebenbefund: derselbe Wert zweimal im Token

`JwtService.cs:110-111` legt die Benutzerkennung doppelt ab:

```csharp
new(JwtRegisteredClaimNames.Sub, user.UserId),
new(ClaimTypes.NameIdentifier,  user.UserId),   // "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
```

`ClaimTypes.NameIdentifier` ist die lange XML-Schema-URI aus WS-Federation. Sie
verdoppelt `sub` und kostet in jedem Token rund siebzig Bytes — bei einem
Anspruch, den `sub` bereits trägt. Kein Fehler, aber Ballast in etwas, das bei
jeder Anfrage über die Leitung geht.

## Vorschlag

**Die Vorgaben streichen.** `EmailVerified` und `AccountStatus` sollten
`bool?` beziehungsweise `string?` sein und ohne Vorgabe kommen. Dann gilt:

- Wer sie setzt, bekommt sie im Token.
- Wer sie nicht setzt, bekommt sie **nicht** im Token — und die beiden Prüfer
  lehnen mangels Anspruch ab, statt eine Konstante zu glauben.

Das ist die sichere Richtung: eine fehlende Aussage darf zu „nein" führen, nie
zu „ja". Heute führt sie bei `ActiveAccount` zu „ja".

Alternativ müsste `CreateAccessToken` verlangen, dass beide gesetzt sind, und
sonst werfen — dann fehlt es ehrlich, statt still falsch zu sein.

## Was es uns kostet

Heute nichts: unser Token wird von `TokenformTests` auf seine Gestalt geprüft,
und wir benutzen weder `EmailVerified` noch `ActiveAccount` als Richtlinie.
Unser eigener Kontostand wird je Anfrage aus der Datenbank gelesen.

**Aber unser Kommentar in `GirderAccessTokenIssuer` sagt „Mehr steht nicht
drin", und das stimmt nicht** — es stehen drei Ansprüche mehr im Token, als
dort aufgezählt sind. Solange dieser Fehler offen ist, kann das nicht anders
werden, ohne an Girder vorbeizubauen.

## Stand

- [ ] gemeldet
- [ ] `EmailVerified` und `AccountStatus` ohne Vorgabe
- [ ] `TokenformTests` prüft die geschlossene Menge statt einzelner Namen
