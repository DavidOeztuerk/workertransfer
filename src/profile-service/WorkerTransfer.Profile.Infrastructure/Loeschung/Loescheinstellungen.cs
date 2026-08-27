namespace WorkerTransfer.Profile.Infrastructure.Loeschung;

/// <summary>Was beweist, dass ein Löschbefehl wirklich aus der Kaskade kam.</summary>
public sealed class Loescheinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>
    /// Das geteilte Geheimnis, das der Ursprung in <c>X-Erasure-Secret</c> zeigt.
    /// </summary>
    /// <remarks>
    /// Ausdrücklich ein anderes als jedes Benachrichtigungsgeheimnis: „darf eine
    /// Mail anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht
    /// dasselbe Papier sein (ADR-0027 §4.4).
    /// <para>
    /// <strong>Leer heißt: der Endpunkt ist zu, nicht offen.</strong> Bei einer
    /// Route, die einen Menschen löscht, wäre eine Voreinstellung, die im
    /// Zweifel öffnet, die schlechteste — und eine nicht gesetzte Variable ist
    /// genau der Zweifelsfall.
    /// </para>
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
