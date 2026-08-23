using WorkerTransfer.Portfolio.Api;
using WorkerTransfer.Portfolio.Infrastructure;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "portfolio-service";

// Kein AlsAussteller(): dieser Dienst prüft Tokens und stellt keine aus.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddPortfolioInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("portfolio")
    ?? throw new InvalidOperationException("ConnectionStrings:portfolio ist nicht gesetzt."));

var app = builder.Build();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapPortfolioEndpoints();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
