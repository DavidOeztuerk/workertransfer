using WorkerTransfer.Notification.Api;
using WorkerTransfer.Notification.Infrastructure;
using WorkerTransfer.Notification.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "notification-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddNotificationInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("notification")
    ?? throw new InvalidOperationException("ConnectionStrings:notification is not configured."));

var app = builder.Build();

// Erst wandern, dann bedienen (ADR-0010).
await app.WandereAsync<NotificationDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapBenachrichtigungsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
