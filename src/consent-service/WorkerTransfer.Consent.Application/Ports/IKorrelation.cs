namespace WorkerTransfer.Consent.Application.Ports;

/// <summary>What this request is filed under.</summary>
/// <remarks>
/// A port rather than a reach into <c>HttpContext</c>, so the audit trail can
/// be written by something that is not a request — the erasure arrives over
/// HTTP today and may not tomorrow.
/// </remarks>
public interface IKorrelation
{
    /// <summary>The id of the running request, where there is one.</summary>
    string? Aktuell { get; }
}
