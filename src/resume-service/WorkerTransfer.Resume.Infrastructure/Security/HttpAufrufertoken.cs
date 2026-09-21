using Microsoft.AspNetCore.Http;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.Security;

/// <summary>Reads the token out of the request that is running.</summary>
/// <remarks>
/// Header first, then the cookie — the same order the services themselves use
/// when they verify one. A browser never sees the token except as an
/// <c>httpOnly</c> cookie, and a service-to-service caller sends the header.
/// </remarks>
public sealed class HttpAufrufertoken(IHttpContextAccessor zugriff) : IAufrufertoken
{
    /// <inheritdoc />
    public string? Wert
    {
        get
        {
            var request = zugriff.HttpContext?.Request;

            if (request is null)
            {
                return null;
            }

            var header = request.Headers.Authorization.ToString();

            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return header["Bearer ".Length..];
            }

            return request.Cookies.TryGetValue("access", out var ausCookie)
                   && !string.IsNullOrEmpty(ausCookie)
                ? ausCookie
                : null;
        }
    }
}
