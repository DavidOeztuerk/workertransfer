namespace WorkerTransfer.Assessment.Infrastructure.Loeschung;

/// <summary>Was beweist, dass ein Löschbefehl wirklich aus der Kaskade kam.</summary>
public sealed class Loescheinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>Das geteilte Geheimnis aus <c>X-Erasure-Secret</c>.</summary>
    /// <remarks>
    /// <strong>Leer heisst: der Endpunkt ist zu, nicht offen.</strong> Bei einer
    /// Route, die die Arbeit und die Rückmeldungen eines Menschen löscht, wäre
    /// eine Voreinstellung, die im Zweifel öffnet, die schlechteste — und eine
    /// nicht gesetzte Variable ist genau der Zweifelsfall.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
