using Girder.Abstractions.Caching;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.RateLimiting;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using WorkerTransfer.Gateway;

// Konfiguration aus der Umgebung. VOR CreateBuilder, weil der
// Konfigurationsaufbau die Umgebungsvariablen genau einmal liest — danach
// geladen hiesse geladen und von niemandem gelesen.
WorkerTransfer.ServiceDefaults.Umgebung.Laden();

var builder = WebApplication.CreateBuilder(args);

// Die Landkarte reist als eigene Datei, nicht in appsettings: sie ist der
// ganze Dienst, und wer sie sucht, soll sie am Namen erkennen.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: false);

builder.Services.AddOcelot(builder.Configuration);

// Die Bremse — Girders, ganz. Hier standen bis Girder 4.2.0 rund hundertdreißig
// eigene Zeilen, und der Grund dafür war nie eine fehlende Fähigkeit:
//
//   * Selektiv bremsen (fünf benannte Pfade, sonst nichts) kann Girder, seit es
//     Grenzen gibt — eine Vorgabe von 0 legt keinen Zähler an. Es stand nur
//     nirgends, und kein Test hielt es fest.
//   * Die Abweisung war kein Problemdokument. Behoben in 4.1.0.
//   * Und sie zu holen kostete `Girder.Infrastructure` mit vierundvierzig
//     transitiven Paketen — Swagger, Telemetrie, neun Logging-Pakete — für ein
//     Gateway, das nur routet. `Girder.Http` bringt NULL mit.
//
// Die fünf Grenzen stehen weiter in `ocelot.json`, neben den Routen, aus denen
// sie ausgewählt sind. `BremsenkarteTests` hält weiterhin fest, dass jeder
// gebremste Pfad wirklich eine Route hat — die Zusage überlebt die Datei.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
builder.Services.Configure<DistributedRateLimitingOptions>(
    builder.Configuration.GetSection(DistributedRateLimitingOptions.SectionName));

var app = builder.Build();

// Alles VOR Ocelot, denn Ocelot beendet die Kette: was danach steht, läuft nie.
// Und in dieser Reihenfolge, jede Stufe aus einem Grund:
//
//   Gesundheit  zuerst und UNGEBREMST. Eine gebremste Probe nähme den Behälter
//               aus dem Lastverteiler — die Bremse wäre dann der Ausfall.
//   Korrelation vor der Bremse, damit auch ein 429 seine Kennung trägt: wer
//               sich beschwert, ausgesperrt worden zu sein, soll eine nennen
//               können.
app.UseGesundheit();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<DistributedRateLimitingMiddleware>();

await app.UseOcelot();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
