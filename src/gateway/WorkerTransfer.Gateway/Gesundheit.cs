namespace WorkerTransfer.Gateway;

/// <summary>Die zwei Proben, die das Gateway selbst beantwortet.</summary>
/// <remarks>
/// <strong>Nicht als Endpunkt, sondern als Zwischenschicht</strong>, und das
/// ist keine Vorliebe: Ocelot beendet die Kette. Ein <c>MapGet</c> steht in der
/// Endpunktschicht, die danach käme — sie läuft nie, und die Probe bekäme das
/// 404 einer fehlenden Route. Gemessen, nicht vermutet.
/// <para>
/// Und sie werden <em>nicht</em> weitergereicht: eine Antwort „ich bin da" darf
/// nicht davon abhängen, ob ein Dienst dahinter antwortet. Sonst kippt ein
/// einzelner kranker Dienst das Gateway aus dem Lastverteiler und nimmt die
/// anderen zehn mit.
/// </para>
/// </remarks>
public static class Gesundheit
{
    /// <summary>Hängt die Proben in die Kette — vor Ocelot.</summary>
    public static IApplicationBuilder UseGesundheit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, weiter) =>
        {
            var pfad = context.Request.Path.Value;

            if (pfad is "/health/live" or "/health/ready")
            {
                await context.Response.WriteAsJsonAsync(
                    new { status = pfad["/health/".Length..] });
                return;
            }

            await weiter();
        });
    }
}
