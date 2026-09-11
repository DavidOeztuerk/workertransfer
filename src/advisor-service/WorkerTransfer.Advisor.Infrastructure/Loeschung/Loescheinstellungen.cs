namespace WorkerTransfer.Advisor.Infrastructure.Loeschung;

/// <summary>Was beweist, dass ein Löschbefehl wirklich aus der Kaskade kam.</summary>
public sealed class Loescheinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>
    /// Das geteilte Geheimnis aus <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Leer heisst: der Endpunkt ist zu, nicht offen.</strong> Bei einer
    /// Route, die einen Menschen löscht, wäre eine Voreinstellung, die im
    /// Zweifel öffnet, die schlechteste — und eine nicht gesetzte Variable ist
    /// genau der Zweifelsfall.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
