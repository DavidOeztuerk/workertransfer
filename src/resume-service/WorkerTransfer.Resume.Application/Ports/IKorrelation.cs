namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>The id of the request being served, for the trail.</summary>
/// <remarks>
/// A port rather than an <c>IHttpContextAccessor</c> in a handler: the
/// application layer states that it wants the thread of one request, and the
/// infrastructure decides that it comes out of a header.
/// </remarks>
public interface IKorrelation
{
    /// <summary><c>null</c> outside a request — a dispatcher pass, for instance.</summary>
    string? Aktuell { get; }
}
