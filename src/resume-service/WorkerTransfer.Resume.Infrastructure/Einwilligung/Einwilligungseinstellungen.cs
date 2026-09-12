namespace WorkerTransfer.Resume.Infrastructure.Einwilligung;

/// <summary>Where the consent ledger answers.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Abschnitt = "Consent";

    /// <summary>The ledger's base address.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>
    /// How long one question may take.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A silent ledger has to become a 503 quickly; hanging
    /// on it would turn one slow dependency into a service that answers
    /// nothing at all.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}
