using WorkerTransfer.Scout.Api;
using WorkerTransfer.Scout.Infrastructure;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "scout-service";

// No AsIssuer(): this service verifies tokens, it does not issue any.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    ArtificialIntelligence.Used);

builder.Services.AddScoutInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("scout")
    ?? throw new InvalidOperationException("ConnectionStrings:scout is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<ScoutDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapScoutEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
