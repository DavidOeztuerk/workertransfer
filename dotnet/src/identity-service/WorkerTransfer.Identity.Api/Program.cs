using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using WorkerTransfer.Identity.Api;
using WorkerTransfer.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "identity-service";

builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, serviceName, infrastructure => infrastructure
        .AddJwtAuthentication()
        .AddPrincipal()
        .AddPasswordHashing()
        .AddTokenSessions()
        .AddSecurityHeaders()
        .AddHealthChecks()
        .AddObservability());

builder.Services.AddIdentityInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("identity")
    ?? throw new InvalidOperationException("ConnectionStrings:identity is not configured."));

var app = builder.Build();

app.UseSharedInfrastructure(builder.Environment, serviceName, pipeline => pipeline
    .UseCorrelationId()
    .UseSecurityHeaders()
    .UseAuth()
    .UsePrincipal()
    .UseHealthCheckEndpoints());

// Ahead of the endpoints and instead of Girder's UseExceptionHandling(): the
// React app reads `detail` out of an RFC 9457 body, and Girder's handler writes
// an envelope of a different shape.
app.UseMiddleware<ProblemDetailsMiddleware>();

app.MapAuthEndpoints();
app.MapRegistrierungsEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
