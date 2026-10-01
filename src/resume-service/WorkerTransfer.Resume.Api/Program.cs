using WorkerTransfer.Resume.Api;
using WorkerTransfer.Resume.Infrastructure;
using WorkerTransfer.Resume.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "resume-service";

// No AsIssuer(): only identity-service holds the private half of the key.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    ArtificialIntelligence.NotUsed);

builder.Services.AddResumeInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("resume")
    ?? throw new InvalidOperationException("ConnectionStrings:resume is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<ResumeDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapResumeEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
