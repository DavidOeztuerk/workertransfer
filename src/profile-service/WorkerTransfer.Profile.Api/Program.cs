using WorkerTransfer.Profile.Api;
using WorkerTransfer.Profile.Infrastructure;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der EnvironmentFile. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "profile-service";

// Kein AsIssuer(): dieser Dienst prüft Tokens, er stellt keine aus. Die
// private Hälfte des Schlüssels liegt bei identity-service, und ein zweiter
// Aussteller wäre eine zweite Job, die einen Principal erzeugen kann —
// unterscheiden könnte sie danach niemand mehr.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddProfileInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("profile")
    ?? throw new InvalidOperationException("ConnectionStrings:profile ist nicht gesetzt."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.MigrateSchemaAsync<ProfileDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapProfilEndpoints();
app.MapInterneEndpoints();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
