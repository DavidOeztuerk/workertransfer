using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
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

// Die Bremse. Der Zähler kommt von Girder, die Kette von hier.
//
// Girders eigene Zwischenschicht ist seit 4.0.0 nachgemessen in Ordnung — sie
// liest die Herkunft allein aus Connection.RemoteIpAddress, ein gefälschtes
// X-Forwarded-For hebt sie nicht mehr auf, sie zählt je Herkunft und kann
// Je-Pfad-Grenzen. Sie steht hier trotzdem nicht, und der Grund ist klein und
// genau benannt: ihre Abweisung ist application/json mit einem traceId — also
// weder Problemdokument noch Korrelationskennung, fest verdrahtet ohne Haken
// (bugs/abweisung-der-bremse-ist-kein-problemdokument.md). Wer sich ausgesperrt
// meldet, soll eine Kennung nennen können. Landet das, fällt diese Kette weg.
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
