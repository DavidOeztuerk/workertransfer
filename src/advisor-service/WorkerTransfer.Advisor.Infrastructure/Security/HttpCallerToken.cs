using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.Advisor.Infrastructure.Security;

/// <summary>The token of the current request.</summary>
/// <remarks>
/// Header first, then cookie — the same order in which the services verify one.
/// A browser sees the token only as an <c>httpOnly</c> cookie; a
/// service-to-service caller sends the header.
/// <para>
/// That this service asks and writes the ledger with <em>this</em> token is not
/// a convenience: the ledger is strictly self-administered, so a stage release
/// can only succeed when the person triggers it themselves.
/// </para>
/// </remarks>
public interface ICallerToken
{
    /// <summary>The raw token value, or <c>null</c>.</summary>
    string? Value { get; }
}

/// <inheritdoc cref="ICallerToken" />
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
                return header["Bearer ".Length..].Trim();
            }

            return request.Cookies.TryGetValue("access", out var fromCookie)
                   && !string.IsNullOrEmpty(fromCookie)
                ? fromCookie
                : null;
        }
    }
}
