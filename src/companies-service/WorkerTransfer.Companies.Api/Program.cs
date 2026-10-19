using WorkerTransfer.Companies.Api;
using WorkerTransfer.Companies.Api.Authorization;
using WorkerTransfer.Companies.Infrastructure;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

// Configuration out of the .env file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "companies-service";

// No AsIssuer(): only identity-service holds the private half.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

// The employer profile is how the company presents itself and stands
// publicly — writing it demands an `admin`. Who that is, the MEMBERSHIP TABLE
// of identity-service decides, per request: a role in the token would take
// effect after a removal only at expiry.
builder.Services.AddAdminPermissions(builder.Configuration, ShopWindowPermissions.Write);

builder.Services.AddCompaniesInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("companies")
    ?? throw new InvalidOperationException("ConnectionStrings:companies is not configured."));

var app = builder.Build();

// Migrate first, serve second (ADR-0010).
await app.MigrateSchemaAsync<CompaniesDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapEmployerEndpoints();

// No MapErasureEndpoints(): this service holds nothing about a natural person
// and therefore does not stand in `ErasureRecipient.Services` (ADR-0027 §2). An
// erasure endpoint here would be one that says "done" without ever having done
// anything.

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
