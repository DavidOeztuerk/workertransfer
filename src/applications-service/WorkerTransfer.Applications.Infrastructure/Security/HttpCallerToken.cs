using Microsoft.AspNetCore.Http;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Security;

/// <summary>Reads the token from the running request.</summary>
/// <remarks>
/// Header first, then cookie — the same order in which the services check one.
/// A browser sees the token only as an <c>httpOnly</c> cookie, a
/// service-to-service caller sends the header.
/// </remarks>
public sealed class HttpCallerToken(IHttpContextAccessor accessor) : ICallerToken
{
    private static readonly AsyncLocal<string?> Override = new();

    /// <summary>Sets the token for the background job.</summary>
    public static IDisposable Use(string? value)
    {
        var before = Override.Value;
        Override.Value = value;
        return new Restorer(before);
    }

    /// <inheritdoc />
    public string? Value
    {
        get
        {
            if (Override.Value is { Length: > 0 } set)
            {
                return set;
            }

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

    private sealed class Restorer(string? before) : IDisposable
    {
        public void Dispose() => Override.Value = before;
    }
}
