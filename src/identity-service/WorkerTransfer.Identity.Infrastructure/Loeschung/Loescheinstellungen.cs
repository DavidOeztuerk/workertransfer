namespace WorkerTransfer.Identity.Infrastructure.Loeschung;

/// <summary>Where the deletion orders go, and what proves they are ours.</summary>
public sealed class Loescheinstellungen
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>
    /// The shared secret for the cascade.
    /// </summary>
    /// <remarks>
    /// Expressly a different one from the notification secret: "may trigger a
    /// mail" and "may delete everything about a person" must not be the same
    /// piece of paper. Empty means nothing is delivered — and as a failure, not
    /// as a silent skip.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>One base address per recipient, keyed by the name in the kind.</summary>
    public Dictionary<string, string> Adressen { get; } = new(StringComparer.Ordinal);
}
