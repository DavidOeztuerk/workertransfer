using WorkerTransfer.Consent.Api;
using WorkerTransfer.Consent.Infrastructure;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "consent-service";

// No AlsAussteller(): only identity-service holds the private half of the key.
// This service verifies tokens and mints none.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddConsentInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("consent")
    ?? throw new InvalidOperationException("ConnectionStrings:consent is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<ConsentDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapEinwilligungsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
