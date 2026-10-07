using WorkerTransfer.Messages.Api;
using WorkerTransfer.Messages.Infrastructure;
using WorkerTransfer.Messages.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "messages-service";

// No AsIssuer(): this service verifies tokens, it does not issue any.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    ArtificialIntelligence.Used);

builder.Services.AddMessagesInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("messages")
    ?? throw new InvalidOperationException("ConnectionStrings:messages is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<MessagesDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapMessageEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
