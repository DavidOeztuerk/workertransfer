using WorkerTransfer.Resume.Api;
using WorkerTransfer.Resume.Infrastructure;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "resume-service";

// No AlsAussteller(): only identity-service holds the private half of the key.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, serviceName);

builder.Services.AddResumeInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("resume")
    ?? throw new InvalidOperationException("ConnectionStrings:resume is not configured."));

var app = builder.Build();

app.UseWorkerTransferDefaults(builder.Environment, serviceName);

app.MapLebenslaufEndpoints();
app.MapLoeschEndpoints();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
