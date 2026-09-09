namespace WorkerTransfer.Jobs.Infrastructure.Rueckzug;

/// <summary>Was beweist, dass ein Rückzugsbefehl wirklich aus der Kaskade kam.</summary>
/// <remarks>
/// Dasselbe Geheimnis wie die Löschung, denn es ist derselbe Ursprung — aber
/// ausdrücklich <b>kein Löschendpunkt</b>: dieser Dienst hält nichts über eine
/// natürliche Person und ist deshalb kein Empfänger der Kaskade (ADR-0027 §2).
/// Was hier ankommt, ist eine Absicht über ein <em>Unternehmen</em>.
/// </remarks>
public sealed class Rueckzugseinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>
    /// Das geteilte Geheimnis aus <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Leer heißt: der Endpunkt ist zu, nicht offen.</strong>
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
