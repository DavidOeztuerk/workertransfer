using WorkerTransfer.Profile.Api;
using WorkerTransfer.Profile.Infrastructure;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "profile-service";

// No AsIssuer(): this service verifies tokens, it does not issue any. The
// private half of the key lies with identity-service, and a second issuer would
// be a second place that can produce a principal — afterwards nobody could tell
// the two apart.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddProfileInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("profile")
    ?? throw new InvalidOperationException("ConnectionStrings:profile is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<ProfileDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapProfileEndpoints();
app.MapInternalEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
