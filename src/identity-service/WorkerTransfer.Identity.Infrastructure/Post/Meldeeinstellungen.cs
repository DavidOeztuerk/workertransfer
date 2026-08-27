namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>Das Geheimnis, mit dem notification-service sich ausweist.</summary>
/// <remarks>
/// Ausdrücklich ein <em>anderes</em> als das der Löschung: „darf eine Mail
/// anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht dasselbe
/// Papier sein (ADR-0027 §4.4). Leer heißt zu.
/// </remarks>
public sealed class Meldeeinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Notify";

    /// <summary>Das gemeinsame Geheimnis.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
