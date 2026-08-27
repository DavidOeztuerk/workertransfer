using WorkerTransfer.Jobs.Api;
using WorkerTransfer.Jobs.Infrastructure;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "jobs-service";

builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddJobsInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("jobs")
    ?? throw new InvalidOperationException("ConnectionStrings:jobs ist nicht gesetzt."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<JobsDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapStellenEndpoints();

// Kein POST /erasure: dieser Dienst haelt nichts ueber eine natuerliche Person
// (ADR-0027 §2). Ein Loeschbefehl an ihn waere ein Endpunkt, der "erledigt"
// sagt, ohne je etwas zu tun. Was er empfaengt, ist der Unternehmensrueckzug.
app.MapRueckzugsEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
