using WorkerTransfer.Jobs.Api;
using WorkerTransfer.Jobs.Api.Authorization;
using WorkerTransfer.Jobs.Infrastructure;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration build reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "jobs-service";

builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    ArtificialIntelligence.Used);

// Two acts bind this company to the outside — putting an advert up and taking
// it down. Who may do that is decided by identity-service's MEMBERSHIP TABLE,
// per request and not by the token: a token lives on after somebody was removed
// from a company, and the removal would then take effect only at expiry.
//
// The list stands HERE and not in `AddWorkerTransferDefaults`: the mechanism
// must not be answered eleven times, the list is a decision.
builder.Services.AddAdminPermissions(
    builder.Configuration,
    JobPermissions.Publish,
    JobPermissions.Close,
    JobPermissions.Sources);

builder.Services.AddJobsInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("jobs")
    ?? throw new InvalidOperationException("ConnectionStrings:jobs is not set."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<JobsDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapSourceEndpoints();
app.MapJobEndpoints();

// No POST /erasure: this service holds nothing about a natural person
// (ADR-0027 §2). An erasure command to it would be an endpoint that says "done"
// without ever doing anything. What it receives is the company withdrawal.
app.MapWithdrawalEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
