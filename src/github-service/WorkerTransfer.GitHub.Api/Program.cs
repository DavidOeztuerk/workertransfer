using WorkerTransfer.GitHub.Api;
using WorkerTransfer.GitHub.Infrastructure;
using WorkerTransfer.GitHub.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration out of the .env file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "github-service";

// No AsIssuer(): only identity-service holds the private half.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddGitHubInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("github")
    ?? throw new InvalidOperationException("ConnectionStrings:github is not configured."));

var app = builder.Build();

// Migrate first, serve second (ADR-0010).
await app.MigrateSchemaAsync<GitHubDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapConnectionEndpoints();
app.MapErasureEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
