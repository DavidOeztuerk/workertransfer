namespace WorkerTransfer.Applications.Application;

/// <summary>Was in der Outbox stehen darf.</summary>
/// <remarks>
/// Eine Art, kein Text. Die Outbox ist dauerhafter Speicher und landet in jeder
/// Sicherung; eine Spalte für den Nachrichtentext wäre eine Einladung, ihn
/// hineinzuschreiben (ADR-0025).
/// </remarks>
public static class Benachrichtigungsarten
{
    /// <summary>
    /// Das Unternehmen hat die Bewerbung bewegt.
    /// </summary>
    /// <remarks>
    /// Nur der Zug des Unternehmens wird gemeldet — der Rückzug durch die
    /// Person nicht: sie weiß, was sie getan hat.
    /// </remarks>
    public const string Bewegt = "application_update";
}
