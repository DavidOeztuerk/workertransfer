using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Was ein Dienst antworten muss, bevor er Verkehr bekommt.</summary>
public static class Bereitschaft
{
    /// <summary>Wie lange eine stumme Datenbank die Probe aufhalten darf.</summary>
    /// <remarks>
    /// Eine Bereitschaftsprobe ohne Zeitlimit haengt so lange wie der
    /// Verbindungsversuch, und der Orchestrierer wertet das Ausbleiben als
    /// gesund, bis sein eigenes Limit greift.
    /// </remarks>
    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(5);

    /// <summary>Meldet den Dienst erst bereit, wenn seine Datenbank antwortet.</summary>
    /// <remarks>
    /// <c>/health/ready</c> filtert nach dem Etikett <c>ready</c>. Ohne eine
    /// einzige so etikettierte Eintragung ist die gefilterte Menge leer, ein
    /// leerer Bericht gilt als gesund, und die Adresse antwortet 200 — auch
    /// ueber einer unerreichbaren Datenbank. Der Orchestrierer schickt dann
    /// Verkehr an einen Dienst, der jede Anfrage mit 500 beantwortet.
    /// </remarks>
    /// <typeparam name="TKontext">Der Kontext dieses Dienstes.</typeparam>
    /// <param name="services">Der Container.</param>
    /// <param name="dienstname">Benennt die Eintragung im Gesundheitsbericht.</param>
    /// <returns>Der Container.</returns>
    public static IServiceCollection AddDatenbankbereitschaft<TKontext>(
        this IServiceCollection services, string dienstname)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dienstname);

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            $"{dienstname}-datenbank",
            anbieter => new Datenbankbereitschaft(
                anbieter.GetRequiredService<TKontext>()),
            HealthStatus.Unhealthy,
            ["ready", "db"],
            Geduld));

        return services;
    }
}

/// <summary>Antwortet die Datenbank dieses Dienstes?</summary>
/// <remarks>
/// <c>CanConnectAsync</c> und keine Abfrage: eine Bereitschaftsprobe laeuft im
/// Sekundentakt, und eine Probe, die selbst Last erzeugt, nimmt einen
/// angeschlagenen Dienst vollends herunter.
/// </remarks>
/// <param name="kontext">Der Kontext, dessen Verbindung geprueft wird.</param>
internal sealed class Datenbankbereitschaft(DbContext kontext) : IHealthCheck
{
    /// <inheritdoc />
    /// <remarks>
    /// Die Beschreibung nennt keinen Wert. Noelia gibt sie auf
    /// <c>/health/ready</c> aus, und diese Adresse antwortet jedem — eine
    /// Npgsql-Ausnahme traegt Wirt, Port und mitunter den Benutzer. Die
    /// Ausnahme reist als zweites Feld mit und landet im Protokoll, nicht in
    /// der Antwort.
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await kontext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Die Datenbank antwortet.")
                : HealthCheckResult.Unhealthy("Die Datenbank antwortet nicht.");
        }
        catch (Exception fehler)
        {
            return HealthCheckResult.Unhealthy("Die Datenbank antwortet nicht.", fehler);
        }
    }
}
