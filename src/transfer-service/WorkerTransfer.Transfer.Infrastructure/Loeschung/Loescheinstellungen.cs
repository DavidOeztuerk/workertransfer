namespace WorkerTransfer.Transfer.Infrastructure.Erasure;

/// <summary>Das Geheimnis, mit dem sich die Löschkaskade ausweist.</summary>
/// <remarks>
/// Ausdrücklich ein <em>anderes</em> als das der Benachrichtigung: „darf eine
/// Mail anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht
/// dasselbe Papier sein (ADR-0027 §4.4). Leer heißt zu — bei einem Endpunkt,
/// der alles über einen Menschen löscht, wäre eine Voreinstellung, die im
/// Zweifel öffnet, die denkbar schlechteste.
/// </remarks>
public sealed class ErasureSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Erasure";

    /// <summary>Das gemeinsame Geheimnis.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
