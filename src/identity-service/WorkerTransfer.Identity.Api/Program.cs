using Girder.Infrastructure.Builder.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using WorkerTransfer.Identity.Api;
using WorkerTransfer.Identity.Api.Berechtigung;
using WorkerTransfer.Identity.Infrastructure;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "identity-service";

// The only service that calls AlsAussteller(): it holds the private half of the
// key. A second issuer would be a second place that can mint a principal, and
// nothing downstream could tell the two apart.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    // AddAuthorization() bringt den PermissionPolicyProvider. OHNE IHN
    // beantwortet niemand die `Permission:`-Namen, und das Geruest lehnt JEDE
    // Anfrage an einen so geschuetzten Endpunkt ab — fail-closed, aber an genau
    // der Stelle, die das Attribut schuetzen sollte. Das faellt erst dem auf,
    // der es zum ersten Mal benutzt, und sieht dann aus wie ein kaputter
    // Endpunkt.
    infrastruktur => infrastruktur.AlsAussteller().AddAuthorization());

// Wer ein Firmenrecht hat, entscheidet die MITGLIEDSCHAFTSTABELLE, nicht das
// Token. Girders eigener Handler bleibt daneben stehen und liest Ansprueche —
// er findet bei uns keine. Beide laufen; ein Succeed genuegt.
//
// Rechte ins Token zu legen waere der kuerzere Weg und der schlechtere: ein
// Token lebt weiter, nachdem jemand aus einer Firma entfernt wurde.
builder.Services.AddScoped<IAuthorizationHandler, Mitgliedschaftsrecht>();

// Eine Ablehnung durch eine Richtlinie schliesst die Antwort kurz und wirft
// nicht — ProblemDetailsMiddleware sieht sie also nie. Ohne das hier faellt sie
// als nackter 403 ohne Korrelationskennung heraus.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, Ablehnungsgestalt>();

builder.Services.AddIdentityInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("identity")
    ?? throw new InvalidOperationException("ConnectionStrings:identity is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<IdentityDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapAuthEndpoints();
app.MapRegistrierungsEndpoints();
app.MapUnternehmensEndpoints();
app.MapMeldeEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
