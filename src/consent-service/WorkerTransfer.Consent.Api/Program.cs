using WorkerTransfer.Consent.Api;
using WorkerTransfer.Consent.Infrastructure;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration out of the EnvironmentFile. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loading
// afterwards would mean loading and nobody reading.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "consent-service";

// No AsIssuer(): only identity-service holds the private half of the key.
// This service verifies tokens and mints none.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddConsentInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("consent")
    ?? throw new InvalidOperationException("ConnectionStrings:consent is not configured."));

var app = builder.Build();

// Migrate first, serve afterwards (ADR-0010).
await app.MigrateSchemaAsync<ConsentDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapConsentEndpoints();
app.MapErasureEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
