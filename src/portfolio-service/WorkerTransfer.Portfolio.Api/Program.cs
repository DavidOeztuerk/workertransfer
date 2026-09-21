using WorkerTransfer.Portfolio.Api;
using WorkerTransfer.Portfolio.Infrastructure;
using WorkerTransfer.Portfolio.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der EnvironmentFile. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "portfolio-service";

// Kein AsIssuer(): dieser Dienst prüft Tokens und stellt keine aus.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddPortfolioInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("portfolio")
    ?? throw new InvalidOperationException("ConnectionStrings:portfolio ist nicht gesetzt."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.MigrateSchemaAsync<PortfolioDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapPortfolioEndpoints();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
