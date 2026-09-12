namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>The identifier that ties one story together across services.</summary>
/// <remarks>
/// A port rather than a read of the request: the audit trail records it, and
/// the application layer must not have to know that a request is HTTP to be
/// able to write down which one it was.
/// </remarks>
public interface IKorrelation
{
    /// <summary><c>null</c> outside a request — a background dispatcher has none.</summary>
    string? Aktuell { get; }
}
