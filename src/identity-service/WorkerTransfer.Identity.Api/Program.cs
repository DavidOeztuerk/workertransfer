using Microsoft.AspNetCore.Authorization;
using WorkerTransfer.Identity.Api;
using WorkerTransfer.Identity.Api.Permissions;
using WorkerTransfer.Identity.Infrastructure;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration out of the EnvironmentFile. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loading
// afterwards would mean loading and nobody reading.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "identity-service";

// The only service that calls AsIssuer(): it holds the private half of the
// key. A second issuer would be a second place that can mint a principal, and
// nothing downstream could tell the two apart.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    girder => girder.AsIssuer());

// Since Girder 4.0.2 the policy machinery comes out of the defaults:
// `NoeliaModule.Authorization` calls `AddAuthorization()` and thereby brings
// the PermissionPolicyProvider — the one that resolves `Permission:*` names at
// run time. Until 4.0.1 the same module called `AddResourceAuthorization()`,
// and the machinery was silently missing; it stood here by hand. Not needed any
// more — CompanyJourneyTests shows it bites all the same.

// Who holds a company permission is decided by the MEMBERSHIP TABLE, not by
// the token. Noelia's own handler stays beside it and reads claims — it finds
// none of ours. Both run; one Succeed is enough.
//
// Putting permissions into the token would be the shorter way and the worse
// one: a token lives on after somebody was removed from a company.
builder.Services.AddScoped<IAuthorizationHandler, MembershipPermission>();

builder.Services.AddIdentityInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("identity")
    ?? throw new InvalidOperationException("ConnectionStrings:identity is not configured."));

var app = builder.Build();

// Migrate first, serve afterwards (ADR-0010).
await app.MigrateSchemaAsync<IdentityDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapAuthEndpoints();
app.MapRegistrationEndpoints();
app.MapCompanyEndpoints();
app.MapNotifyEndpoints();
app.MapErasureEndpoints();
app.MapInternalEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
