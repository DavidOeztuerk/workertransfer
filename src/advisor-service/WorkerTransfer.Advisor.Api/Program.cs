using WorkerTransfer.Advisor.Api.Authorization;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Advisor.Api;
using WorkerTransfer.Advisor.Infrastructure;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "advisor-service";

// No AsIssuer(): this service verifies tokens, it does not issue any.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName,
    ArtificialIntelligence.NotUsed);

// The first contact to a person is a recruiter's (ADR-0048). Who that is,
// identity-service's membership table says per request (`Identity__BaseUrl`,
// `Identity__Secret`); without them the route answers 503, not 403.
builder.Services.AddCompanyPermissions(
    builder.Configuration, [], [AdvisorPermissions.Open]);

builder.Services.AddAdvisorInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("advisor")
    ?? throw new InvalidOperationException("ConnectionStrings:advisor is not configured."));

var app = builder.Build();

// Migrate first, then serve (ADR-0010).
await app.MigrateSchemaAsync<AdvisorDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapAdvisorEndpoints();
app.MapErasureEndpoint();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
