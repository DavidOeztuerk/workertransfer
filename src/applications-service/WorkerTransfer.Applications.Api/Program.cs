using WorkerTransfer.Applications.Api;
using WorkerTransfer.Applications.Infrastructure;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "applications-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddApplicationsInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("applications")
    ?? throw new InvalidOperationException("ConnectionStrings:applications is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<ApplicationsDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapBewerbungsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
