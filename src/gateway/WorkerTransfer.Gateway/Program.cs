using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using WorkerTransfer.Gateway;

var builder = WebApplication.CreateBuilder(args);

// Die Landkarte reist als eigene Datei, nicht in appsettings: sie ist der
// ganze Dienst, und wer sie sucht, soll sie am Namen erkennen.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: false);

builder.Services.AddOcelot(builder.Configuration);

// Die Bremse. Der Zähler kommt von Girder und ist nachgemessen richtig; seine
// Zwischenschicht nicht — die lässt in 3.0.1 alles durch
// (bugs/distributed-ratelimiting-middleware-bremst-nicht.md). Also der Speicher
// von dort, die Kette von hier.
//
// `InMemoryRateLimitStore` zählt IM PROZESS. Das bindet das Gateway an
// replicaCount: 1 — der Ausweg ist ein Registrierungswechsel auf
// `RedisDistributedRateLimitStore`, kein Umbau.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection(Bremseinstellungen.Abschnitt)
        .Get<Bremseinstellungen>() ?? new Bremseinstellungen());

var app = builder.Build();

// Alles VOR Ocelot, denn Ocelot beendet die Kette: was danach steht, läuft nie.
// Und in dieser Reihenfolge, jede Stufe aus einem Grund:
//
//   Gesundheit  zuerst und UNGEBREMST. Eine gebremste Probe nähme den Behälter
//               aus dem Lastverteiler — die Bremse wäre dann der Ausfall.
//   Korrelation vor der Bremse, damit auch ein 429 seine Kennung trägt: wer
//               sich beschwert, ausgesperrt worden zu sein, soll eine nennen
//               können.
//   Bremse      vor Navigation, weil Navigation den Pfad auf `/__ui/...`
//               umschreibt. Danach träfe keine Regel mehr zu.
app.UseGesundheit();
app.UseKorrelation();
app.UseBremse();
app.UseNavigation();

await app.UseOcelot();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
