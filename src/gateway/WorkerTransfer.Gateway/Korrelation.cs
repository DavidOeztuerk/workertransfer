namespace WorkerTransfer.Gateway;

/// <summary>Gibt jeder Anfrage eine Kennung, bevor sie sich aufteilt.</summary>
/// <remarks>
/// Das Einzige, was dieses Gateway an einer Anfrage <em>tut</em> — und es ist
/// keine Prüfung, sondern Klempnerei. Ein Klick im Browser wird hinten zu drei
/// Aufrufen an drei Dienste; ohne eine gemeinsame Kennung erfindet jeder seine
/// eigene, und die drei Protokollzeilen lassen sich nachher nicht mehr
/// zusammenlegen.
/// <para>
/// Eine mitgebrachte Kennung wird durchgereicht statt überschrieben: sie kommt
/// dann von einem Aufrufer, der schon eine Kette führt. Der Wert ist nie eine
/// Berechtigung — er entscheidet nichts, er benennt nur.
/// </para>
/// </remarks>
public static class Korrelation
{
    /// <summary>Der Kopf, den Girders Zwischenschicht in jedem Dienst liest.</summary>
    public const string Kopf = "X-Correlation-ID";

    /// <summary>Hängt die Klempnerei in die Kette.</summary>
    public static IApplicationBuilder UseKorrelation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, weiter) =>
        {
            if (!context.Request.Headers.TryGetValue(Kopf, out var mitgebracht)
                || string.IsNullOrWhiteSpace(mitgebracht.ToString()))
            {
                context.Request.Headers[Kopf] = Guid.CreateVersion7().ToString("N");
            }

            // Auch zurück an den Browser: wer einen Fehler meldet, kann die
            // Kennung nennen, und dann findet man die Zeile.
            context.Response.Headers[Kopf] = context.Request.Headers[Kopf];

            await weiter();
        });
    }
}
