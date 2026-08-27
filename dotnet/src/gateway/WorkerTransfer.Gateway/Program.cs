using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using WorkerTransfer.Gateway;

var builder = WebApplication.CreateBuilder(args);

// Die Landkarte reist als eigene Datei, nicht in appsettings: sie ist der
// ganze Dienst, und wer sie sucht, soll sie am Namen erkennen.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: false);

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

// Alles drei VOR Ocelot, denn Ocelot beendet die Kette: was danach steht, läuft
// nie. Und in dieser Reihenfolge — die Gesundheitsprobe soll weder eine
// Kennung erfinden noch als Navigation gelten.
app.UseGesundheit();
app.UseKorrelation();
app.UseNavigation();

await app.UseOcelot();

await app.RunAsync();

/// <summary>Benennt den Einstiegspunkt für den Testwirt.</summary>
public partial class Program;
