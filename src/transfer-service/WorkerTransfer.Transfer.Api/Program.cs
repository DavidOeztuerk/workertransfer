using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Api;
using WorkerTransfer.Transfer.Api.Authorization;
using WorkerTransfer.Transfer.Infrastructure;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "transfer-service";

// No AsIssuer(): only identity-service holds the private half.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

// Two actions bind the company: an offer (date and fee) and the completion.
// Who may take them, the MEMBERSHIP TABLE of identity-service decides, per
// request — a role in the token would take effect after a removal only at
// expiry.
//
// `withdraw` is deliberately not among them: whoever may start must be allowed
// to stop.
builder.Services.AddAdminPermissions(
    builder.Configuration,
    CasePermissions.Offer,
    CasePermissions.Complete);

builder.Services.AddTransferInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("transfer")
    ?? throw new InvalidOperationException("ConnectionStrings:transfer is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<TransferDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapMarketEndpoints();
app.MapCaseEndpoints();
app.MapErasureEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
