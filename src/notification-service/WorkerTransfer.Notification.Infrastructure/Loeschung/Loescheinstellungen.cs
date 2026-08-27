namespace WorkerTransfer.Notification.Infrastructure.Loeschung;

/// <summary>Das Geheimnis, mit dem sich die Löschkaskade ausweist.</summary>
/// <remarks>
/// Ausdrücklich ein <em>anderes</em> als das der Benachrichtigung: „darf eine
/// Mail anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht
/// dasselbe Papier sein (ADR-0027 §4.4). Leer heißt zu.
/// </remarks>
public sealed class Loescheinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>Das gemeinsame Geheimnis.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
