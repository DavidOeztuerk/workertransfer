namespace WorkerTransfer.Transfer.Application;

/// <summary>What may stand in the outbox.</summary>
/// <remarks>
/// A kind, not a text. The outbox is durable storage and ends up in every
/// backup; a column for the message text would be an invitation to write it in
/// (ADR-0025). At this service that weighs especially heavily: the message
/// would be about somebody being approachable.
/// </remarks>
public static class NotificationKinds
{
    /// <summary>A company asked for the market status.</summary>
    public const string Requested = "market_request";

    /// <summary>A case moved.</summary>
    public const string Moved = "transfer_update";
}
