using WorkerTransfer.Scout.Api;
using WorkerTransfer.Scout.Infrastructure;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "scout-service";

// Kein AlsAussteller(): dieser Dienst prüft Tokens, er stellt keine aus.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddScoutInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("scout")
    ?? throw new InvalidOperationException("ConnectionStrings:scout ist nicht gesetzt."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<ScoutDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapScoutEndpoints();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
