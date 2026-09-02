using Girder.Abstractions.Caching;
using Girder.Abstractions.Hosting;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Headers;
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

// Die Bremse. Der Zähler kommt von Girder und ist nachgemessen richtig; keine
// seiner DREI Zwischenschichten taugt hier — zwei bremsen nicht, die dritte ist
// nirgends verdrahtet, und alle drei glauben X-Forwarded-For bedingungslos
// (bugs/ratenbegrenzung-drei-wege-zwei-bremsen-nicht.md). Also der Speicher von
// dort, die Kette von hier.
//
// `InMemoryRateLimitStore` zählt IM PROZESS. Das bindet das Gateway an
// replicaCount: 1 — der Ausweg ist ein Registrierungswechsel auf
// `RedisDistributedRateLimitStore`, kein Umbau.
// Girder, aber nur EIN Modul.
//
// Das Gateway ruft `AddWorkerTransferDefaults` bewusst nicht: es ist kein
// Dienst, es hat keine Datenbank, keine CQRS-Kette und prueft kein Token. Was
// ihm bisher trotzdem fehlte, waren die Sicherheitskoepfe — gemessen: `GET
// /health/live` direkt am Gateway kam ohne `X-Content-Type-Options`, ohne
// `X-Frame-Options`, ohne CSP zurueck. Dieselbe Anfrage DURCHGEREICHT an einen
// Dienst brachte alle mit, denn sie stammten vom Dienst und nie vom Gateway.
//
// Betroffen war alles, was das Gateway SELBST beantwortet: die Gesundheits-
// proben und jede 429 der Bremse. Also ausgerechnet die Antworten, die ein
// Aufrufer am ehesten zu sehen bekommt, ohne je einen Dienst erreicht zu haben.
builder.Services.AddGirder(
    builder.Configuration,
    builder.Environment,
    "gateway",
    girder => girder.Use(GirderModule.SecurityHeaders));

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
// GANZ vorn, vor der Gesundheitsprobe: die Koepfe sollen auf JEDER Antwort
// stehen, auch auf denen, die hier gleich enden. Eine Stufe, die eine Antwort
// schreibt, kommt fuer sie zu spaet.
app.UseSecurityHeaders();

app.UseGesundheit();
app.UseKorrelation();
app.UseBremse();
app.UseNavigation();

await app.UseOcelot();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
