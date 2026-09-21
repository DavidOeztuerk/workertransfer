using Noelia.Abstractions.Caching;
using Noelia.Infrastructure.Middleware;
using Noelia.Infrastructure.Models;
using Noelia.Infrastructure.RateLimiting;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using WorkerTransfer.Gateway;

// Configuration from the environment file. BEFORE CreateBuilder, because the
// configuration builder reads the environment variables exactly once — loading
// afterwards would mean loading and nobody reading.
WorkerTransfer.ServiceDefaults.EnvironmentFile.Load();

var builder = WebApplication.CreateBuilder(args);

// The route map travels as a file of its own, not inside appsettings: it is the
// whole service, and whoever looks for it should recognise it by its name.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: false);

builder.Services.AddOcelot(builder.Configuration);

// The brake — Noelia's, entirely. Until Girder 4.2.0 some hundred and thirty
// lines of our own stood here, and the reason for them was never a missing
// capability:
//
//   * Braking selectively (five named paths, nothing else) is something Noelia
//     can do ever since there have been limits — a default of 0 writes no
//     counter. It merely stood nowhere, and no test pinned it.
//   * Its rejection was not a problem document. Fixed in 4.1.0.
//   * And fetching it cost `Noelia.Infrastructure` with forty-four transitive
//     packages — Swagger, telemetry, nine logging packages — for a gateway that
//     only routes. `Noelia.Http` brings NONE with it.
//
// The five limits still stand in `ocelot.json`, next to the routes they are
// selected from. `BremsenkarteTests` still pins that every braked path really
// has a route — the promise outlives the file.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
builder.Services.Configure<DistributedRateLimitingOptions>(
    builder.Configuration.GetSection(DistributedRateLimitingOptions.SectionName));

var app = builder.Build();

// Everything BEFORE Ocelot, because Ocelot ends the chain: whatever stands
// after it never runs. And in this order, every step for a reason:
//
//   Health      first and UNBRAKED. A braked probe would take the container out
//               of the load balancer — the brake would then be the outage.
//   Correlation before the brake, so that a 429 carries its id too: whoever
//               complains about being locked out should be able to name one.
app.UseGesundheit();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<DistributedRateLimitingMiddleware>();

await app.UseOcelot();

await app.RunAsync();

/// <summary>Names the entry point for the test host.</summary>
public partial class Program;
