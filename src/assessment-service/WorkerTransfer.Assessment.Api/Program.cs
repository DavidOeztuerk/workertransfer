using WorkerTransfer.Assessment.Api;
using WorkerTransfer.Assessment.Infrastructure;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "assessment-service";

// Kein AlsAussteller(): dieser Dienst prüft Tokens, er stellt keine aus.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddAssessmentInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("assessment")
    ?? throw new InvalidOperationException("ConnectionStrings:assessment ist nicht gesetzt."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<AssessmentDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapArbeitsprobenEndpoints();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
