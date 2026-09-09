using Microsoft.AspNetCore.Http;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>Reads the correlation id Girder's middleware put on the request.</summary>
/// <remarks>
/// The key is the one <c>CorrelationIdMiddleware</c> writes. Falling back to
/// the header would answer with something a caller sent rather than with what
/// this request was actually filed under.
/// </remarks>
public sealed class HttpKorrelation(IHttpContextAccessor zugriff) : IKorrelation
{
    /// <inheritdoc />
    public string? Aktuell =>
        zugriff.HttpContext?.Items.TryGetValue("CorrelationId", out var wert) == true
            ? wert?.ToString()
            : null;
}
