# Die Prüfung noelia.m2m.caller-policy läuft beim Start vor den Routen und meldet einen falschen Stand

- **Noelia-Fassung:** 7.1.1 (von nuget.org)
- **Gefunden beim:** Übernahme der Dienst-Tokens in WorkerTransfer, Gate G-0001, Quest Q-0043, 05.11.2026
- **Art:** Fehler
- **Blockiert:** nein für Merge und CI; `make evidence-check` ist lokal rot, solange der Fehler besteht

## Was passiert

Der Bericht unter `/noelia/report.json` zeigt für den Aussteller:

```
Fail: The issuer is composed but 0 token endpoints are mapped;
exactly one MapServiceTokenEndpoint() is expected.
```

Der Endpoint ist aber gemappt und antwortet. Dieselbe Prüfung liefert nach dem Start das richtige Ergebnis:

```
startup=Fail | rerun=Pass: 7 service door(s) are covered by a fallback, and the refusal handler is in place.
```

Vermutete Ursache, aus dem Quelltext und der Messung abgeleitet: `SecurityCheckStartupService` ist ein `IHostedService` und führt die Prüfungen in `StartAsync` aus. Hosted Services starten vor dem `GenericWebHostService`, und erst der baut die Pipeline, die die Routen von `WebApplication` in die `EndpointDataSource` des Containers überträgt. Die Prüfung sieht darum beim Start keinen Endpoint. `ISecurityCheckReport.Latest` hält dieses Ergebnis, und der Bericht zeigt es.

Das hat zwei Folgen:

- **Der Aussteller meldet immer Fail**, auch wenn alles stimmt.
- **Ein reiner Empfänger meldet vermutlich immer Pass über 0 Türen.** Das folgt aus demselben Ablauf, gemessen ist es nur für den Aussteller. Es wäre die schlimmere Hälfte: Ein Pass, der nichts geprüft hat. Ein `AllowAnonymous` an einer Tür oder eine Tür ohne Fallback fiele im Bericht nicht auf.

Noelias eigene Tests rufen die Prüfung nach dem Start auf und sehen das deshalb nicht.

## Warum es Noelias ist

Gemessen am 05.11.2026 mit Noelia 7.1.1 von nuget.org, ohne WorkerTransfer-Code:

```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["JwtSettings:Issuer"] = "issuer",
    ["JwtSettings:Audience"] = "people",
    ["NOELIA_M2M_CLIENT_CALLER_SECRET_HASH"] = ServiceClientSecret.Generate("caller").Hash,
});

builder.Services.AddNoelia(builder.Configuration, builder.Environment, "repro", noelia => noelia
    .UseDefaults()
    .UseJwt(jwt => jwt.Issue(privateKey, "k"))
    .UseServiceTokenIssuer(issuer => issuer.Clients(builder.Configuration, "caller").Audiences("door"))
    .UseServiceCallers("door")
    .Without(NoeliaModule.RateLimiting, "not the subject of this measurement"));

await using var app = builder.Build();
app.UseNoelia(builder.Environment, "repro");

var doors = app.MapGroup("/internal");
doors.MapGet("/door", () => "open").RequireServiceCaller("caller");
doors.MapServiceFallback();
app.MapServiceTokenEndpoint();

await app.StartAsync();

var startup = app.Services.GetRequiredService<ISecurityCheckReport>().Latest
    .Single(result => result.Id == "noelia.m2m.caller-policy");
var rerun = (await app.Services.GetRequiredService<ISecurityCheckRunner>().RunAsync())
    .Single(result => result.Id == "noelia.m2m.caller-policy");
```

Ergebnis:

```
startup=Fail: The issuer is composed but 0 token endpoints are mapped; exactly one MapServiceTokenEndpoint() is expected.
rerun=Warning: The token endpoint /m2m/token has no rate limit.
```

Das Warning ist in diesem Testfall richtig, er setzt bewusst keine Bremse.

Das README sagt: „The check `noelia.m2m.caller-policy` […] verifies the fallback, the 404 handler and the single token endpoint." Im laufenden Bericht tut er das nicht.

## Was es WorkerTransfer kostet

`make evidence-check` geht für identity-service auf rot, mit einem Befund, der nicht stimmt. Die CI führt das Tor nicht aus, es läuft lokal über `make validate` mit Stack.

**Kein Umweg in WorkerTransfer.** Die Tests führen die Prüfung nach dem Start aus (`The_self_check_of_the_doors_passes` in Identity, Notification und Profile) und belegen so die Verdrahtung. Eine Ausnahme in `scripts/evidence-check.sh` wäre ein Umweg, der das Tor für genau diese Prüfung abschaltet.
