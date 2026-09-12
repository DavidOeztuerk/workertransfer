namespace WorkerTransfer.Profile.Infrastructure.Intern;

/// <summary>Was beweist, dass eine interne Anfrage wirklich von einem Dienst kam.</summary>
/// <remarks>
/// Dasselbe geteilte Geheimnis wie an jeder anderen internen Tür
/// (<c>X-Notify-Secret</c>), und ausdrücklich <em>nicht</em> das der Löschung:
/// „darf Profile durchsuchen" und „darf alles über einen Menschen löschen"
/// dürfen nicht dasselbe Papier sein.
/// <para>
/// <strong>Leer heisst: die Tür ist zu, nicht offen.</strong> Eine nicht
/// gesetzte Variable ist der Zweifelsfall, und bei einer Route, die Profile
/// herausgibt, wäre eine Voreinstellung, die im Zweifel öffnet, die
/// schlechteste.
/// </para>
/// </remarks>
public sealed class Meldeeinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Notify";

    /// <summary>Das gemeinsame Geheimnis.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
