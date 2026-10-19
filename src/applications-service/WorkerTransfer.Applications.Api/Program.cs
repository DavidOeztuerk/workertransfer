using WorkerTransfer.Applications.Api;
using WorkerTransfer.Applications.Infrastructure;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "applications-service";

// No AsIssuer(): only identity-service holds the private half of the key.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddApplicationsInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("applications")
    ?? throw new InvalidOperationException("ConnectionStrings:applications is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<ApplicationsDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapApplicationEndpoints();
app.MapDraftEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
