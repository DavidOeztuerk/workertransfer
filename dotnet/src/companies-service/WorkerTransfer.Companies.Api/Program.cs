using WorkerTransfer.Companies.Api;
using WorkerTransfer.Companies.Infrastructure;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "companies-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddCompaniesInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("companies")
    ?? throw new InvalidOperationException("ConnectionStrings:companies is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<CompaniesDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapArbeitgeberEndpoints();

// Kein MapLoeschEndpoints(): dieser Dienst hält nichts über einen natürlichen
// Menschen und steht deshalb nicht in `Loeschempfaenger.Fremde` (ADR-0027 §2).
// Ein Löschendpunkt hier wäre einer, der „erledigt" sagt, ohne je etwas getan
// zu haben.

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
