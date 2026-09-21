using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.Assessment.Infrastructure.Sicherheit;

/// <summary>Das Token der laufenden ResumeRequest.</summary>
/// <remarks>
/// Header zuerst, dann Cookie — dieselbe Reihenfolge, in der die Dienste eines
/// prüfen. Ein Browser sieht das Token nur als <c>httpOnly</c>-Cookie, ein
/// Dienst-zu-Dienst-Aufrufer schickt den Header.
/// </remarks>
public interface IAufrufertoken
{
    /// <summary>Der rohe Tokenwert, oder <c>null</c>.</summary>
    string? Wert { get; }
}

/// <inheritdoc cref="IAufrufertoken" />
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
                return header["Bearer ".Length..].Trim();
            }

            return request.Cookies.TryGetValue("access", out var ausCookie)
                   && !string.IsNullOrEmpty(ausCookie)
                ? ausCookie
                : null;
        }
    }
}
