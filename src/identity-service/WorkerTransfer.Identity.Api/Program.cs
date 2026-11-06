using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Noelia.Infrastructure.Builder;
using Noelia.Infrastructure.Security.ServiceTokens;
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
    ArtificialIntelligence.Used,
    noelia => noelia
        .AsIssuer()
        .UseServiceTokenIssuer(issuer => issuer
            .Clients(builder.Configuration, ServiceClients.All)
            .Audiences(ServiceAudiences.All))
        .UseServiceCallers(ServiceAudiences.Identity));

// The one brake inside a service (ADR-0057). `.Without(RateLimiting)` holds for
// everything behind the gateway, which sees one origin. `/m2m/token` has no
// gateway route: services call it directly, each from an address of its own.
const string tokenBrake = "m2m-token";
builder.Services.AddRateLimiter(limiter =>
{
    // The framework's default is 503, which a caller reads as "service down".
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(tokenBrake, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 600,
            Window = TimeSpan.FromMinutes(1)
        }));
});

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

// Behind the defaults, so that a refusal carries its correlation id.
app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapExternalLoginEndpoints();
app.MapRegistrationEndpoints();
app.MapCompanyEndpoints();
app.MapErasureEndpoints();

// ONE group with ONE fallback: two fallbacks on `/internal` would be two equal
// routes, and an unknown path would end in an AmbiguousMatchException, a 500.
var doors = app.MapGroup("/internal");
doors.MapNotifyEndpoints();
doors.MapInternalEndpoints();
doors.MapServiceFallback();

app.MapServiceTokenEndpoint().RequireRateLimiting(tokenBrake);

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
