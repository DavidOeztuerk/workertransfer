using WorkerTransfer.GitHub.Api;
using WorkerTransfer.GitHub.Infrastructure;
using WorkerTransfer.GitHub.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "github-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddGitHubInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("github")
    ?? throw new InvalidOperationException("ConnectionStrings:github is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<GitHubDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapVerbindungsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
