using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Was beweist, dass jemand den Nachweis dieses Dienstes sehen darf.</summary>
/// <remarks>
/// <para><strong>Ein eigenes Papier, und das ist eine Entscheidung.</strong>
/// Die internen Türen haben ihr Geheimnis (<c>Notify</c>), die Löschkaskade
/// ihres (<c>Erasure</c>), und schon dort gilt: „darf Profile durchsuchen" und
/// „darf alles über einen Menschen löschen" dürfen nicht dasselbe Papier sein.
/// Der Nachweis ist ein drittes Recht — er hält nichts über einen Menschen,
/// aber er sagt einem Fremden, welche Anbieter diese Instanz benutzt und wo
/// ihre Türen offen stehen. Wer Mails anstoßen darf, muss das nicht erfahren.</para>
///
/// <para><strong>Leer heißt: die Tür ist zu.</strong> Eine nicht gesetzte
/// Variable ist der Zweifelsfall, und im Zweifel zu. Ohne Berechtigung
/// antwortet die Tür mit <strong>404</strong> und nicht mit 403: eine
/// Betriebsoberfläche, deren Existenz man erraten kann, ist selbst schon eine
/// Auskunft — sie sagt, dass es hier etwas zu holen gibt.</para>
/// </remarks>
public sealed class Nachweisgeheimnis
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Nachweis";

    /// <summary>Der Kopf, in dem das Geheimnis vorgezeigt wird.</summary>
    public const string Kopf = "X-Nachweis-Secret";

    /// <summary>Das Geheimnis. Leer heißt: zu.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Hängt den Nachweis an die Dienstgrundlage.</summary>
/// <remarks>
/// <para><strong>Was hier steht, steht hier, weil es für jeden Dienst gleich
/// ist</strong> — der Lauf, die Tür und die eine Prüfung, die jeder Dienst
/// beantworten kann, weil sie nur seine Konfiguration liest
/// (<c>wt.grenze.ziele</c>). Alles andere ist eine Entscheidung dieses Dienstes
/// und steht in seinem Verbundpunkt, wo ein Leser sie sieht — dieselbe Linie,
/// die <c>AddWorkerTransferDefaults</c> für die Noelia-Module zieht.</para>
///
/// <para><strong>Und es ist eine Registrierung und kein Baumeister.</strong>
/// ADR-0003 hat den fluenten <c>PlatformBuilder</c> abgelehnt; eine Prüfung
/// kommt deshalb als <c>services.AddScoped&lt;IPruefung, …&gt;()</c> hinzu,
/// wie alles andere auch. Vierzehn Dienste, die dieselbe Kette anders
/// aufrufen, wären vierzehn Gelegenheiten, sie anders zu meinen.</para>
/// </remarks>
public static class Nachweisaufbau
{
    /// <summary>Lauf, Tür und die Prüfung, die jeder Dienst beantworten kann.</summary>
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Die Konfiguration des Dienstes.</param>
    /// <param name="dienstname">Wie er sich nennt.</param>
    /// <returns>Der Container.</returns>
    public static IServiceCollection AddNachweis(
        this IServiceCollection services,
        IConfiguration configuration,
        string dienstname)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Nachweisgeheimnis>(
            configuration.GetSection(Nachweisgeheimnis.Abschnitt));

        // Die eine Pruefung, die keine Entscheidung eines Dienstes ist: sie
        // liest die Konfiguration, und die hat jeder. Sie steht hier und nicht
        // vierzehnmal, damit ein neuer Dienst sie nicht vergessen kann — das
        // Gegenstueck zur Liste in `LoeschempfaengerTests`, nur dass hier
        // nichts zu pflegen ist.
        services.AddScoped<IPruefung>(_ => new Zielpruefung(configuration));

        services.AddScoped(anbieter => new Nachweislauf(
            dienstname,
            anbieter.GetServices<IPruefung>(),
            anbieter.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }
}
