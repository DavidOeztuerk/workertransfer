using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Rollen;
using WorkerTransfer.Transfer.Api;
using WorkerTransfer.Transfer.Api.Berechtigung;
using WorkerTransfer.Transfer.Infrastructure;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "transfer-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

// Zwei Handlungen binden das Unternehmen: ein Angebot (Termin und Gebuehr) und
// der Abschluss. Wer sie darf, entscheidet die MITGLIEDSCHAFTSTABELLE von
// identity-service, je Anfrage — eine Rolle im Token wirkte nach einer
// Entfernung erst beim Ablauf.
//
// `withdraw` steht absichtlich nicht dabei: wer anfangen darf, muss aufhoeren
// duerfen.
builder.Services.AddAdminrechte(
    builder.Configuration,
    Vorgangsrechte.Anbieten,
    Vorgangsrechte.Abschliessen);

builder.Services.AddTransferInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("transfer")
    ?? throw new InvalidOperationException("ConnectionStrings:transfer is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<TransferDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapMarktEndpoints();
app.MapVorgangsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
