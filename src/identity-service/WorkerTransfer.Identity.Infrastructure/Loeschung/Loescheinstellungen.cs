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

    /// <summary>Wie lange auf einen Empfaenger gewartet wird.</summary>
    /// <remarks>
    /// Fuenf Sekunden. Ohne diese Zeile gilt die Vorgabe von <c>HttpClient</c>
    /// mit HUNDERT Sekunden — bei acht Empfaengern also ueber dreizehn Minuten
    /// fuer einen Durchlauf, in dem einer haengt.
    /// <para>
    /// Kurz zu sein kostet hier nichts: eine Zeitueberschreitung ist ein
    /// Fehlschlag, und ein Fehlschlag laesst die Outbox-Zeile stehen. Sie wird
    /// wiederholt, ohne Versuchsobergrenze (ADR-0027). Was NICHT passieren
    /// darf, ist das Gegenteil — dass ein haengender Empfaenger als zugestellt
    /// gilt.
    /// </para>
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}
