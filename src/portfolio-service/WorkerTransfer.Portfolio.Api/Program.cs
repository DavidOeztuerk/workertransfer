using WorkerTransfer.Portfolio.Api;
using WorkerTransfer.Portfolio.Infrastructure;
using WorkerTransfer.Portfolio.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration out of the .env file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "portfolio-service";

// No AsIssuer(): this service verifies tokens and issues none.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddPortfolioInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("portfolio")
    ?? throw new InvalidOperationException("ConnectionStrings:portfolio ist nicht gesetzt."));

var app = builder.Build();

// Migrate first, serve second (ADR-0010).
await app.MigrateSchemaAsync<PortfolioDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapPortfolioEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
