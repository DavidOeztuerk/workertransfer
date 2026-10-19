using Microsoft.AspNetCore.Http;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Security;

/// <summary>Reads the token out of the running request.</summary>
/// <remarks>
/// Header first, then cookie — the same order in which the services verify
/// one. A browser sees the token only as an <c>httpOnly</c> cookie; a
/// service-to-service caller sends the header.
/// </remarks>
public sealed class HttpCallerToken(IHttpContextAccessor accessor) : ICallerToken
{
    /// <inheritdoc />
    public string? Value
    {
        get
        {
            var request = accessor.HttpContext?.Request;

            if (request is null)
            {
                return null;
            }

            var header = request.Headers.Authorization.ToString();

            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return header["Bearer ".Length..];
            }

            return request.Cookies.TryGetValue("access", out var fromCookie)
                   && !string.IsNullOrEmpty(fromCookie)
                ? fromCookie
                : null;
        }
    }
}
