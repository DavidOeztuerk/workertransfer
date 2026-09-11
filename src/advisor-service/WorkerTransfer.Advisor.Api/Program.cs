using WorkerTransfer.Advisor.Api;
using WorkerTransfer.Advisor.Infrastructure;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "advisor-service";

// Kein AlsAussteller(): dieser Dienst prüft Tokens, er stellt keine aus.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddAdvisorInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("advisor")
    ?? throw new InvalidOperationException("ConnectionStrings:advisor ist nicht gesetzt."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<AdvisorDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapBeraterEndpoints();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
