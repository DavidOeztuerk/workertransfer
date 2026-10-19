using WorkerTransfer.Notification.Api;
using WorkerTransfer.Notification.Infrastructure;
using WorkerTransfer.Notification.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

// Configuration out of the .env file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loaded
// afterwards would mean loaded and read by nobody.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "notification-service";

// No AsIssuer(): only identity-service holds the private half.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddNotificationInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("notification")
    ?? throw new InvalidOperationException("ConnectionStrings:notification is not configured."));

var app = builder.Build();

// Migrate first, serve second (ADR-0010).
await app.MigrateSchemaAsync<NotificationDbContext>();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapNotificationEndpoints();
app.MapErasureEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
