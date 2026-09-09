namespace WorkerTransfer.Profile.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger erreichbar ist.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    /// <remarks>
    /// Im Compose-Netz der Dienstname, nicht <c>localhost</c> — der Aufruf läuft
    /// von Behälter zu Behälter.
    /// </remarks>
    public string Adresse { get; set; } = "http://consent-service:8002";

    /// <summary>
    /// Wie lange auf eine Antwort gewartet wird.
    /// </summary>
    /// <remarks>
    /// Kurz, und das ist Absicht: eine ausbleibende Antwort wird zu 503, und
    /// ein 503 nach fünf Sekunden ist eine Auskunft, ein 503 nach sechzig eine
    /// hängende Oberfläche.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}
