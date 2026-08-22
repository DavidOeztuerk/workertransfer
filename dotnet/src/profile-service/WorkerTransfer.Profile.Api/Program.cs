using WorkerTransfer.Profile.Api;
using WorkerTransfer.Profile.Infrastructure;
using WorkerTransfer.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
const string dienstname = "profile-service";

// Kein AlsAussteller(): dieser Dienst prüft Tokens, er stellt keine aus. Die
// private Hälfte des Schlüssels liegt bei identity-service, und ein zweiter
// Aussteller wäre eine zweite Stelle, die einen Principal erzeugen kann —
// unterscheiden könnte sie danach niemand mehr.
builder.Services.AddWorkerTransferDefaults(
    builder.Configuration, builder.Environment, dienstname);

builder.Services.AddProfileInfrastructure(
    builder.Configuration,
    builder.Configuration.GetConnectionString("profile")
    ?? throw new InvalidOperationException("ConnectionStrings:profile ist nicht gesetzt."));

var app = builder.Build();

app.UseWorkerTransferDefaults(builder.Environment, dienstname);

app.MapProfilEndpoints();
app.MapEntwurfsEndpunkt();
app.MapLoeschEndpunkt();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
