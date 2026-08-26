using WorkerTransfer.Applications.Api;
using WorkerTransfer.Applications.Infrastructure;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "applications-service";

// Kein AlsAussteller(): nur identity-service hält die private Hälfte.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddApplicationsInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("applications")
    ?? throw new InvalidOperationException("ConnectionStrings:applications is not configured."));

var app = builder.Build();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapBewerbungsEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
