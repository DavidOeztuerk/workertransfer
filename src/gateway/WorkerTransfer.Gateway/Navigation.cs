using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WorkerTransfer.Gateway;

/// <summary>Trennt „ein Mensch geht auf eine Seite" von „ein Programm holt Daten".</summary>
/// <remarks>
/// <c>/jobs</c> heißt zwei verschiedene Dinge: die API-Ressource <em>und</em>
/// die Seite der Oberfläche. Dasselbe gilt für <c>/applications</c>,
/// <c>/transfers</c> und <c>/github</c>. Nach Pfad allein ist das nicht zu
/// unterscheiden.
/// <para>
/// <c>Sec-Fetch-Dest: document</c> schickt der Browser <strong>ausschließlich</strong>
/// bei einer Navigation der obersten Ebene; <c>fetch</c>/XHR schicken
/// <c>empty</c>, und curl, CLI und Dienst-zu-Dienst schicken den Kopf gar
/// nicht.
/// </para>
/// <para>
/// <strong>Warum das hier steht und nicht in der Landkarte:</strong> gemessen
/// an Ocelot 25 gewinnt eine <em>wörtliche</em> Route immer gegen einen
/// Platzhalter — <c>Priority</c> hin oder her. Eine Auffangregel
/// <c>/{alles}</c> mit Kopfbedingung wird von <c>/jobs</c> also nie erreicht.
/// Die Regel je kollidierendem Pfad zu wiederholen wäre möglich und wäre genau
/// das, was sie vermeiden soll: zwei Pfadlisten, die auseinanderlaufen.
/// Stattdessen setzt diese Zwischenschicht <em>ein</em> Präfix, und die
/// Landkarte hat dafür <em>eine</em> Zeile.
/// </para>
/// </remarks>
public static class Navigation
{
    /// <summary>Der Kopf, den nur ein navigierender Browser schickt.</summary>
    public const string Kopf = "Sec-Fetch-Dest";

    /// <summary>Der Wert, der eine Navigation der obersten Ebene bedeutet.</summary>
    public const string Dokument = "document";

    /// <summary>
    /// Wofür der Browser sonst noch fragt, wenn er eine Seite zusammensetzt.
    /// </summary>
    /// <remarks>
    /// <para>Ohne diese Liste kam die Seite an und blieb <strong>leer</strong>:
    /// das Gateway lieferte das HTML, aber jedes <c>&lt;script src="/src/main.tsx"&gt;</c>
    /// darin lief ins Leere. Gemessen im Browser — vier 404 für
    /// <c>/@vite/client</c>, <c>/@react-refresh</c>, <c>/config.js</c> und
    /// <c>/src/main.tsx</c>.</para>
    ///
    /// <para><strong>Warum eine Liste von Zwecken und keine von Pfaden.</strong>
    /// Die naheliegende Antwort wären Routen für <c>/@vite/*</c>, <c>/src/*</c>,
    /// <c>/node_modules/*</c>, <c>/assets/*</c> — also eine Aufzählung dessen,
    /// was ein Bündler heute erzeugt. Die ist beim nächsten Werkzeugwechsel
    /// falsch, und sie unterscheidet sich zwischen Entwicklungsserver und
    /// gebautem Bündel. Der Kopf hier sagt stattdessen, <em>wofür</em> gefragt
    /// wird, und das ändert sich nicht.</para>
    ///
    /// <para><strong>Und es trennt sauber von der API.</strong> Ein
    /// <c>fetch()</c> schickt <c>empty</c>, steht also nicht auf dieser Liste
    /// und bleibt bei den Diensten. Nur was der Browser <em>zum Zusammenbauen
    /// einer Seite</em> holt, geht an die Oberfläche.</para>
    ///
    /// <para>Ein Fremder kann den Kopf fälschen und damit statische Dateien der
    /// Oberfläche erreichen. Das ist kein Verlust: sie sind öffentlich, und wer
    /// sie will, bekommt sie ohnehin.</para>
    ///
    /// <para><strong>Aber nur, wo kein Dienst den Pfad beansprucht.</strong> Ein
    /// erster Anlauf schrieb jeden Bestandteil um — und schickte damit auch
    /// <c>/jobs</c> an die Oberfläche, sobald jemand <c>Sec-Fetch-Dest: script</c>
    /// mitgab. <c>Alles_andere_als_document_geht_an_den_Dienst</c> fiel darüber,
    /// zu Recht: was ein Dienst beansprucht, gehört dem Dienst, egal wofür
    /// gefragt wird. Nur ein Dokument gehört immer der Oberfläche, denn eine
    /// Adresse wie <c>/jobs</c> ist auch eine Seite.</para>
    /// </remarks>
    public static readonly string[] Bestandteile =
        ["script", "style", "image", "font", "worker"];

    /// <summary>
    /// Die ersten Wegabschnitte, die eine Route in <c>ocelot.json</c> beansprucht.
    /// </summary>
    /// <remarks>
    /// Aus der Landkarte gelesen und nicht danebengeschrieben: eine zweite
    /// Liste über dieselben Pfade geht beim ersten neuen Endpunkt auseinander.
    /// </remarks>
    public static HashSet<string> BeanspruchteAbschnitte(IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);

        var abschnitte = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in konfiguration.GetSection("Routes").GetChildren())
        {
            var vorlage = route["UpstreamPathTemplate"];

            if (string.IsNullOrEmpty(vorlage))
            {
                continue;
            }

            var erster = vorlage.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            // Ein Platzhalter beansprucht nichts Bestimmtes.
            if (erster is not null && !erster.StartsWith('{'))
            {
                abschnitte.Add(erster);
            }
        }

        return abschnitte;
    }

    /// <summary>
    /// Das Präfix, unter dem die Landkarte die Oberfläche führt.
    /// </summary>
    /// <remarks>
    /// Zwei Unterstriche, damit es keine echte Adresse der Oberfläche
    /// verdeckt: <c>/__ui/profile</c> ist keine Seite, die jemand tippt.
    /// </remarks>
    public const string Praefix = "/__ui";

    /// <summary>Hängt die Regel in die Kette — vor Ocelot, das sie beendet.</summary>
    public static IApplicationBuilder UseNavigation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var beansprucht = BeanspruchteAbschnitte(
            app.ApplicationServices.GetRequiredService<IConfiguration>());

        return app.Use(async (context, weiter) =>
        {
            // Von außen mitgebracht darf das Präfix nicht sein: es ist eine
            // Markierung dieses Gateways, keine Adresse. Wer es tippt, bekommt
            // dieselbe Antwort wie auf jeden anderen unbekannten Pfad.
            if (context.Request.Path.StartsWithSegments(Praefix))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var zweck = context.Request.Headers[Kopf].ToString();

            var erster = context.Request.Path.Value?
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            var derOberflaeche =
                string.Equals(zweck, Dokument, StringComparison.Ordinal)
                || (Bestandteile.Contains(zweck, StringComparer.Ordinal)
                    && (erster is null || !beansprucht.Contains(erster)));

            if (derOberflaeche)
            {
                context.Request.Path = Praefix + context.Request.Path;
            }

            await weiter();
        });
    }
}
