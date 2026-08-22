using Girder.Infrastructure.Builder.Modules;
using WorkerTransfer.Identity.Api;
using WorkerTransfer.Identity.Infrastructure;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "identity-service";

// The only service that calls AlsAussteller(): it holds the private half of the
// key. A second issuer would be a second place that can mint a principal, and
// nothing downstream could tell the two apart.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    infrastruktur => infrastruktur.AlsAussteller());

builder.Services.AddIdentityInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("identity")
    ?? throw new InvalidOperationException("ConnectionStrings:identity is not configured."));

var app = builder.Build();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapAuthEndpoints();
app.MapRegistrierungsEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
