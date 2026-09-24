namespace WorkerTransfer.Applications.Application;

/// <summary>What may stand in the outbox.</summary>
/// <remarks>
/// A kind, not a text. The outbox is durable storage and ends up in every
/// backup; a column for the message text would be an invitation to write it in
/// (ADR-0025).
/// </remarks>
public static class NotificationKinds
{
    /// <summary>
    /// The company has moved the application.
    /// </summary>
    /// <remarks>
    /// Only the company's move is reported — the person's withdrawal is not:
    /// she knows what she did.
    /// </remarks>
    public const string Moved = "application_update";

    /// <summary>
    /// An application has arrived at a company.
    /// </summary>
    /// <remarks>
    /// Goes to the company's MEMBERS, not to the company itself. A company has
    /// no inbox.
    /// </remarks>
    public const string Received = "application_received";
}
