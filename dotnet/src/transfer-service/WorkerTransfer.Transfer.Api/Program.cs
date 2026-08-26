using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Api;
using WorkerTransfer.Transfer.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "transfer-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddTransferInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("transfer")
    ?? throw new InvalidOperationException("ConnectionStrings:transfer is not configured."));

var app = builder.Build();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapMarktEndpoints();
app.MapVorgangsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
