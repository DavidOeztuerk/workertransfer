using Microsoft.AspNetCore.Http;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Security;

/// <summary>Liest das Token aus der laufenden Anfrage.</summary>
/// <remarks>
/// Kopf zuerst, dann Cookie — dieselbe Reihenfolge, in der die Dienste eines
/// prüfen. Ein Browser sieht das Token nur als <c>httpOnly</c>-Cookie, ein
/// Dienst-zu-Dienst-Aufrufer schickt den Kopf.
/// </remarks>
public sealed class HttpAufrufertoken(IHttpContextAccessor zugriff) : IAufrufertoken
{
    /// <inheritdoc />
    public string? Wert
    {
        get
        {
            var anfrage = zugriff.HttpContext?.Request;

            if (anfrage is null)
            {
                return null;
            }

            var kopf = anfrage.Headers.Authorization.ToString();

            if (kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return kopf["Bearer ".Length..];
            }

            return anfrage.Cookies.TryGetValue("access", out var ausCookie)
                   && !string.IsNullOrEmpty(ausCookie)
                ? ausCookie
                : null;
        }
    }
}
