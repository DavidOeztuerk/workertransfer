using WorkerTransfer.Applications.Api;
using WorkerTransfer.Applications.Infrastructure;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der EnvironmentFile. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "applications-service";

// Kein AsIssuer(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddApplicationsInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("applications")
    ?? throw new InvalidOperationException("ConnectionStrings:applications is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.MigrateSchemaAsync<ApplicationsDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapBewerbungsEndpoints();
app.MapEntwurfsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
