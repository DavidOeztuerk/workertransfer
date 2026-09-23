using Microsoft.AspNetCore.Http;
using WorkerTransfer.Consent.Application.Ports;

namespace WorkerTransfer.Consent.Infrastructure.Security;

/// <summary>Reads the correlation id Noelia's middleware put on the request.</summary>
/// <remarks>
/// The key is the one <c>CorrelationIdMiddleware</c> writes. Falling back to
/// the header would file the request under something a caller sent rather than
/// under what it was actually filed as.
/// </remarks>
public sealed class HttpCorrelation(IHttpContextAccessor accessor) : ICorrelation
{
    /// <inheritdoc />
    public string? Current =>
        accessor.HttpContext?.Items.TryGetValue("CorrelationId", out var value) == true
            ? value?.ToString()
            : null;
}
