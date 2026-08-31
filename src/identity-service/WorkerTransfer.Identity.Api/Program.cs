using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using WorkerTransfer.Identity.Api;
using WorkerTransfer.Identity.Api.Berechtigung;
using WorkerTransfer.Identity.Infrastructure;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "identity-service";

// The only service that calls AlsAussteller(): it holds the private half of the
// key. A second issuer would be a second place that can mint a principal, and
// nothing downstream could tell the two apart.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    girder => girder.AlsAussteller());

// Die Richtlinienmaschinerie kommt seit Girder 4.0.2 aus der Vorgabe:
// `GirderModule.Authorization` ruft `AddAuthorization()` und bringt damit den
// PermissionPolicyProvider mit — den, der `Permission:*`-Namen zur Laufzeit
// aufloest. Bis 4.0.1 rief dasselbe Modul `AddResourceAuthorization()`, und die
// Maschinerie fehlte still; sie stand hier von Hand. Jetzt nicht mehr noetig —
// UnternehmensreiseTests belegt, dass sie trotzdem greift.

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
