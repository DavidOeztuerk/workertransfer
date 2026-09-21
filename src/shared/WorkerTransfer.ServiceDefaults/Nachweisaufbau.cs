using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Noelia.Infrastructure.Audit;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Was beweist, dass jemand die Betriebsoberfläche sehen darf.</summary>
/// <remarks>
/// <para><strong>Ein eigenes Papier, und das ist eine Entscheidung.</strong> Die
/// internen Türen haben ihr Geheimnis (<c>Notify</c>), die Löschkaskade ihres
/// (<c>Erasure</c>), und schon dort gilt: „darf Profile durchsuchen" und „darf
/// alles über einen Menschen löschen" dürfen nicht dasselbe Papier sein.
/// <c>/noelia</c> ist ein drittes Recht — es hält nichts über einen Menschen,
/// aber es sagt einem Fremden, welche Anbieter diese Instanz benutzt und wo ihre
/// Türen offen stehen. Wer Mails anstoßen darf, muss das nicht erfahren.</para>
///
/// <para><strong>Leer heißt: die Tür ist zu.</strong> Eine nicht gesetzte
/// Variable ist der Zweifelsfall, und im Zweifel zu. Noelias Dashboard
/// antwortet dann mit <strong>404</strong> und nicht mit 403 — eine
/// Betriebsoberfläche, deren Existenz man erraten kann, ist selbst schon eine
/// Auskunft.</para>
///
/// <para><strong>Der Name blieb, der Inhalt nicht.</strong> Bis ADR-0045 stand
/// hier der Aufbau für sieben selbstgebaute <c>/nachweis/…</c>-Adressen. Die
/// liefert Noelia; übrig bleibt die Tür davor und die Registrierung der
/// Prüfungen, die dieses Produkt selbst beantwortet.</para>
/// </remarks>
public sealed class Nachweisgeheimnis
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Nachweis";

    /// <summary>Der Kopf, in dem das Geheimnis vorgezeigt wird.</summary>
    /// <remarks>
    /// Der Name, den die Control Plane sendet. Ein eigener Name hier hiesse,
    /// dass sie diese Flotte nicht einsammeln kann.
    /// </remarks>
    public const string Kopf = "X-Noelia-Operator";

    /// <summary>Das Geheimnis. Leer heißt: zu.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Hängt den Nachweis an die Dienstgrundlage.</summary>
/// <remarks>
/// <para>Was hier steht, steht hier, weil es für jeden Dienst gleich ist: die
/// Tür. Welche Prüfungen ein Dienst beantwortet, ist eine Entscheidung
/// <em>dieses</em> Dienstes und steht in seinem Verbundpunkt, wo ein Leser sie
/// sieht — dieselbe Linie, die <c>AddWorkerTransferDefaults</c> für die Module
/// zieht.</para>
///
/// <para><strong>Noelias Prüfungen sind Singletons</strong>
/// (<c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;ISecurityCheck, …&gt;)</c>).
/// Wer eine eigene registriert und dabei etwas Bereichsgebundenes hineinzieht,
/// baut eine gefangene Abhängigkeit — ein <c>DbContext</c>, der ewig lebt. Die
/// Prüfungen hier nehmen deshalb den <c>IServiceProvider</c> und öffnen ihren
/// eigenen Bereich.</para>
/// </remarks>
public static class Nachweisaufbau
{
    /// <summary>Die Tür vor der Betriebsoberfläche.</summary>
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Die Konfiguration des Dienstes.</param>
    /// <returns>Der Container.</returns>
    public static IServiceCollection AddNachweis(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Nachweisgeheimnis>(
            configuration.GetSection(Nachweisgeheimnis.Abschnitt));

        // Das Dashboard haelt fest, wer es gelesen hat, und verweigert die
        // Antwort, wenn es das nicht kann. Ohne diese Zeile meldet die
        // Zugriffspruefung eine Warnung statt eines Passes.
        services.AddSovereignAuditTrail();

        return services;
    }

    /// <summary>Darf dieser Aufrufer die Betriebsoberfläche sehen?</summary>
    /// <remarks>
    /// <para><c>FixedTimeEquals</c> und nicht <c>==</c>: ein Vergleich, der beim
    /// ersten falschen Zeichen aufhört, verrät über seine Dauer, wie viele
    /// Zeichen stimmten. Derselbe Vergleich wie an jeder anderen internen Tür
    /// dieses Baumes.</para>
    ///
    /// <para>Sie ist <c>public</c>, weil Noelias <c>VisibleTo(...)</c> sie als
    /// Prädikat nimmt — und <c>static</c>, weil das Prädikat bei der
    /// Komposition übergeben wird und keinen Zustand haben darf.</para>
    /// </remarks>
    /// <param name="context">Die Anfrage.</param>
    /// <returns>Ob sie eingelassen wird.</returns>
    public static bool Eingelassen(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var geheimnis = context.RequestServices
            .GetRequiredService<IOptions<Nachweisgeheimnis>>().Value.Geheimnis;

        if (string.IsNullOrEmpty(geheimnis)
            || !context.Request.Headers.TryGetValue(
                Nachweisgeheimnis.Kopf, out var vorgelegt))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(vorgelegt.ToString()),
            Encoding.UTF8.GetBytes(geheimnis));
    }
}
