using WorkerTransfer.Jobs.Api;
using WorkerTransfer.Jobs.Api.Berechtigung;
using WorkerTransfer.Jobs.Infrastructure;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Rollen;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "jobs-service";

builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

// Zwei Handlungen binden dieses Unternehmen nach aussen — eine Anzeige
// hinstellen und sie wegnehmen. Wer das darf, entscheidet die
// MITGLIEDSCHAFTSTABELLE von identity-service, je Anfrage und nicht das Token:
// ein Token lebt weiter, nachdem jemand aus einer Firma entfernt wurde, und die
// Entfernung wirkte dann erst beim Ablauf.
//
// Die Liste steht HIER und nicht in `AddWorkerTransferDefaults`: der Mechanismus
// darf nicht elfmal beantwortet werden, die Liste ist eine Entscheidung.
builder.Services.AddAdminrechte(
    builder.Configuration,
    Stellenrechte.Veroeffentlichen,
    Stellenrechte.Schliessen);

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
