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

            if (string.Equals(
                    context.Request.Headers[Kopf].ToString(),
                    Dokument,
                    StringComparison.Ordinal))
            {
                context.Request.Path = Praefix + context.Request.Path;
            }

            await weiter();
        });
    }
}
