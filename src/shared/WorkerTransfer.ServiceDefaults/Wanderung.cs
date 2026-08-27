using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Erst wandern, dann bedienen.</summary>
/// <remarks>
/// Jeder Dienst besitzt sein eigenes Schema (ADR-0010) und wandert nur sich
/// selbst — es gibt keinen zentralen Schritt, der alle zehn kennen müsste. Das
/// ist es, was <c>docker compose up</c> auf einem frischen Klon genügen lässt:
/// kein Skript, kein <c>psql</c> von Hand, keine Reihenfolge zum Merken.
/// <para>
/// <strong>Beim Start und nicht in einem eigenen Behälter</strong>, anders als
/// zur Python-Zeit: <c>dotnet ef</c> braucht das SDK, und ein Laufzeitbild hat
/// keins. Der Preis ist eine vierte Begründung für <c>replicaCount: 1</c> —
/// zwei Pilger auf demselben Schema sind ein Rennen, das niemand gewinnt. Wer
/// die Zahl anhebt, muss vorher hierhin zurückkommen.
/// </para>
/// <para>
/// <strong>Abschaltbar über <c>Datenbank:Wandern</c>.</strong> Nicht für
/// Bequemlichkeit, sondern für den Fall, dass ein Betreiber die Wanderung
/// bewusst von Hand fährt: dann soll ein Neustart sie nicht doch anwerfen. Die
/// Voreinstellung ist an, denn ein Dienst, der ohne sein Schema startet,
/// antwortet auf jede Anfrage mit einem Fehler.
/// </para>
/// </remarks>
public static class Wanderung
{
    /// <summary>Der Schalter in der Konfiguration.</summary>
    public const string Schluessel = "Datenbank:Wandern";

    /// <summary>Wie lange auf eine Datenbank gewartet wird, die noch hochfährt.</summary>
    /// <remarks>
    /// <strong>Nicht Bequemlichkeit, sondern Notwendigkeit.</strong> In Compose
    /// meldet <c>pg_isready</c> „bereit", während Postgres noch seine
    /// Anlegeskripte auf einem nur lokal hörenden Server fährt — der Dienst
    /// bekommt dann „connection refused". In Kubernetes gibt es
    /// <c>depends_on</c> überhaupt nicht: dort startet der Behälter, wann er
    /// will. Ein Dienst, der daran stirbt, ist in beiden Umgebungen falsch.
    /// <para>
    /// Eine Minute ist eine Wahl, keine Ableitung: lang genug für ein Postgres,
    /// das zum ersten Mal hochkommt, kurz genug, dass eine echte Fehlkonfiguration
    /// nicht als Hängen erscheint. Danach fliegt der Fehler — <em>nicht</em>
    /// stillschweigend weiterbedienen: ein Dienst ohne sein Schema antwortet auf
    /// jede Anfrage mit einem Fehler, und das wäre der schlechtere Zustand.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Geduld = TimeSpan.FromMinutes(1);

    /// <summary>Wandert das Schema dieses Dienstes, falls eingeschaltet.</summary>
    /// <typeparam name="TKontext">Der Kontext, dem das Schema gehört.</typeparam>
    /// <param name="app">Die gebaute Anwendung.</param>
    public static async Task WandereAsync<TKontext>(this WebApplication app)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue(Schluessel, defaultValue: true))
        {
            return;
        }

        await using var bereich = app.Services.CreateAsyncScope();

        var protokoll = bereich.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(Wanderung));

        protokoll.LogInformation("Wandere das Schema von {Kontext}", typeof(TKontext).Name);

        var kontext = bereich.ServiceProvider.GetRequiredService<TKontext>();
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        var pause = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                await kontext.Database.MigrateAsync();
                return;
            }
            catch (Exception fehler) when (Vorruebergehend(fehler) && uhr.Elapsed < Geduld)
            {
                // Die Art des Fehlers, nie die Verbindungszeichenfolge: darin
                // steht ein Passwort, und ein Protokoll ist auch ein Ort, an
                // dem etwas landet.
                protokoll.LogWarning(
                    "Datenbank noch nicht bereit ({Art}), erneut in {Pause}s",
                    fehler.GetType().Name, pause.TotalSeconds);

                await Task.Delay(pause);

                // Wachsend, aber gedeckelt: die ersten Versuche sollen schnell
                // greifen, die späteren nicht im Sekundentakt hämmern.
                pause = TimeSpan.FromSeconds(Math.Min(pause.TotalSeconds * 2, 8));
            }
        }
    }

    /// <summary>Ist das eine Datenbank, die noch hochfährt — oder ein Fehler?</summary>
    /// <remarks>
    /// <strong>Die Unterscheidung ist der ganze Punkt.</strong> Der erste
    /// Entwurf fing jede Ausnahme, und ein echter Schemafehler wurde dadurch zu
    /// einer Minute Warten mit einer irreführenden letzten Meldung: die
    /// Wanderung lief neun Mal erneut an und meldete am Ende „Typ existiert
    /// bereits" — angelegt vom ersten Versuch, dessen wahre Ursache niemand
    /// mehr sah.
    /// <para>
    /// <c>DbException.IsTransient</c> ist die Frage, die der Anbieter selbst
    /// beantwortet; ein <c>SocketException</c> in der Kette ist der Fall, in
    /// dem noch gar niemand geantwortet hat.
    /// </para>
    /// </remarks>
    private static bool Vorruebergehend(Exception fehler)
    {
        for (var glied = fehler; glied is not null; glied = glied.InnerException)
        {
            if (glied is System.Net.Sockets.SocketException)
            {
                return true;
            }

            if (glied is System.Data.Common.DbException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
