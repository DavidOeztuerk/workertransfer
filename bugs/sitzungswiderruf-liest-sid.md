# Ein Sitzungswiderruf greift nie: der Aussteller schreibt `session_id`, die Prüfung liest `sid`

- **Noelia-Fassung:** 7.0.0
- **Gefunden beim:** Widerruf in allen Diensten über Valkey (`docs/VISION-FRAGEN.md`, I1), 06.10.2026
- **Art:** Fehler
- **Blockiert:** nein — WorkerTransfer widerruft beim Abmelden das einzelne Token
  (`jti`) und beim Löschen alles vor einem Zeitpunkt (Subjekt-Stichtag). Beides
  liest der Prüfer richtig. Nur „diese eine Sitzung beenden“ geht nicht.

## Was passiert

`JwtService` schreibt die Sitzung als Claim **`session_id`** ins Token
(`Noelia.Infrastructure/Security/JwtService.cs`, Zeile 151):

```csharp
claims.Add(new("session_id", user.SessionId));
```

Die Prüfung liest sie als **`sid`** — an zwei Stellen:

- `Noelia.Infrastructure/Security/TokenRevocationMiddleware.cs`, Zeile 95:
  `SessionId = user.FindFirst("sid")?.Value`
- `Noelia.Infrastructure/Security/JwtService.cs`, Zeile 344, dieselbe Zeile.

Ein Token aus Noelias eigenem Aussteller hat damit für die Prüfung **nie eine
Sitzung**. `ITokenRevocationWriter.RevokeSessionAsync(subject, session, …)`
schreibt den Eintrag, der Prüfer fragt ihn mit `SessionId = null` nie ab, und
das Token gilt weiter. Registriert, vorhanden, ohne Wirkung.

Erwartet: Nach `RevokeSessionAsync` wird jedes Token dieser Sitzung abgewiesen.

## Warum es Noelias ist

Nur Noelia-Pakete, kein WorkerTransfer-Code:

```csharp
// Pakete: Noelia.Infrastructure 7.0.0, Noelia.InMemory 7.0.0
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Noelia.Abstractions.Security;
using Noelia.InMemory.Security;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection().AddInMemoryTokenRevocation().BuildServiceProvider();
var writer = services.GetRequiredService<ITokenRevocationWriter>();
var evaluator = services.GetRequiredService<ITokenRevocationEvaluator>();

// So sieht ein von JwtService ausgestelltes Token aus (Claim-Namen wie in Zeile 151).
var principal = new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("jti", "t1"),
    new Claim("sub", "anna"),
    new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
    new Claim("session_id", "s1"),
], "test"));

await writer.RevokeSessionAsync("anna", "s1", DateTimeOffset.UtcNow.AddMinutes(15), "probe");

// Was TokenRevocationMiddleware.TryReadToken daraus macht:
var token = new TokenIdentity
{
    TokenId = principal.FindFirst("jti")!.Value,
    SubjectId = principal.FindFirst("sub")!.Value,
    IssuedAt = DateTimeOffset.UtcNow,
    SessionId = principal.FindFirst("sid")?.Value,   // null
};

var verdict = await evaluator.EvaluateAsync(token);
Console.WriteLine(verdict.IsRevoked);   // False — erwartet True
```

`TokenIdentity.SessionId` sagt in seiner eigenen XML-Doku: „Session or device the
token belongs to, from the `sid` claim. Null when the issuer sets no such claim“
— Noelias eigener Aussteller setzt ihn aber, nur unter anderem Namen.

## Zwei Nebenbefunde aus derselben Messung

1. **Noelias Standardkette prüft Widerrufe nicht.** `UseNoelia(environment,
   serviceName)` enthält kein `UseTokenRevocation()`. Wer einen Speicher
   registriert und sich auf die Kette verlässt, prüft nichts — und
   `noelia.revocation.degradation` meldet trotzdem `Pass` („A token-revocation
   evaluator is registered“). WorkerTransfer ruft `app.UseTokenRevocation()`
   deshalb selbst (`ServiceFoundation.UseWorkerTransferDefaults`).
2. **Die Abweisung ist kein Problem-Dokument.** `TokenRevocationMiddleware.Refuse`
   schreibt `401` mit `text/plain` („Token has been revoked“), nicht RFC 9457 mit
   `correlationId`. Die Oberfläche wertet nur den Status aus, also bricht nichts;
   es ist aber die einzige Antwort im Baum ohne Problem-Dokument.

## Was es uns kostet

Heute nichts, weil wir keine einzelne Sitzung widerrufen. Sobald es eine Seite
„Meine Sitzungen → diese abmelden“ gibt, braucht sie die Behebung. Ein Umweg
(einen zweiten Claim `sid` selbst setzen) wird bewusst nicht gebaut.

## Stand

- [x] gemeldet (dieses Ticket, 06.10.2026)
- [ ] in Noelia behoben, Fassung: <…>
- [ ] Umweg hier entfernt — keiner gebaut
