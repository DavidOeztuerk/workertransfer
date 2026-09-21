using WorkerTransfer.Companies.Api;
using WorkerTransfer.Companies.Api.Berechtigung;
using WorkerTransfer.Companies.Infrastructure;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

// Konfiguration aus der EnvironmentFile. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "companies-service";

// Kein AsIssuer(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

// Das EmployerProfile ist die Selbstdarstellung des Unternehmens und steht
// oeffentlich — es zu schreiben verlangt einen `admin`. Wer das ist, entscheidet
// die MITGLIEDSCHAFTSTABELLE von identity-service, je ResumeRequest: eine Rolle im
// Token wirkte nach einer Entfernung erst beim Ablauf.
builder.Services.AddAdminrechte(builder.Configuration, ShopWindowPermissions.Schreiben);

builder.Services.AddCompaniesInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("companies")
    ?? throw new InvalidOperationException("ConnectionStrings:companies is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.MigrateSchemaAsync<CompaniesDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapArbeitgeberEndpoints();

// Kein MapLoeschEndpoints(): dieser Dienst hält nichts über einen natürlichen
// Menschen und steht deshalb nicht in `ErasureRecipient.Fremde` (ADR-0027 §2).
// Ein Löschendpunkt hier wäre einer, der „erledigt" sagt, ohne je etwas getan
// zu haben.

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
