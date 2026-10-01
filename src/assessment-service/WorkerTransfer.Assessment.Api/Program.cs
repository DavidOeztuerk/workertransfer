using WorkerTransfer.Assessment.Api;
using WorkerTransfer.Assessment.Infrastructure;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "assessment-service";

// No AsIssuer(): this service verifies tokens, it does not issue any.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    ArtificialIntelligence.NotUsed);

builder.Services.AddAssessmentInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("assessment")
    ?? throw new InvalidOperationException("ConnectionStrings:assessment is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<AssessmentDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapAssessmentEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
