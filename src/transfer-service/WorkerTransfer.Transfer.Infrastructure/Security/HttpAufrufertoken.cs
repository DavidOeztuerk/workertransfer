using Microsoft.AspNetCore.Http;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Security;

/// <summary>Liest das Token aus der laufenden ResumeRequest.</summary>
/// <remarks>
/// Header zuerst, dann Cookie — dieselbe Reihenfolge, in der die Dienste eines
/// prüfen. Ein Browser sieht das Token nur als <c>httpOnly</c>-Cookie, ein
/// Dienst-zu-Dienst-Aufrufer schickt den Header.
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
