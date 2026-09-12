namespace WorkerTransfer.Transfer.Application;

/// <summary>Was in der Outbox stehen darf.</summary>
/// <remarks>
/// Eine Art, kein Text. Die Outbox ist dauerhafter Speicher und landet in jeder
/// Sicherung; eine Spalte für den Nachrichtentext wäre eine Einladung, ihn
/// hineinzuschreiben (ADR-0025). Bei diesem Dienst wiegt das besonders schwer:
/// die Nachricht handelte davon, dass jemand ansprechbar ist.
/// </remarks>
public static class Benachrichtigungsarten
{
    /// <summary>Ein Unternehmen hat nach dem Marktstatus gefragt.</summary>
    public const string Angefragt = "market_request";

    /// <summary>Ein Vorgang hat sich bewegt.</summary>
    public const string Bewegt = "transfer_update";
}
