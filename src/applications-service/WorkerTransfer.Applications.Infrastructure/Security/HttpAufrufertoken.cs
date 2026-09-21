using Microsoft.AspNetCore.Http;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Security;

/// <summary>Liest das Token aus der laufenden ResumeRequest.</summary>
/// <remarks>
/// Header zuerst, dann Cookie — dieselbe Reihenfolge, in der die Dienste eines
/// prüfen. Ein Browser sieht das Token nur als <c>httpOnly</c>-Cookie, ein
/// Dienst-zu-Dienst-Aufrufer schickt den Header.
/// </remarks>
public sealed class HttpAufrufertoken(IHttpContextAccessor zugriff) : IAufrufertoken
{
    private static readonly AsyncLocal<string?> Uebersteuert = new();

    /// <summary>Setzt das Token für den Hintergrundauftrag.</summary>
    public static IDisposable Mit(string? value)
    {
        var vorher = Uebersteuert.Value;
        Uebersteuert.Value = value;
        return new Rueckgabe(vorher);
    }

    /// <inheritdoc />
    public string? Wert
    {
        get
        {
            if (Uebersteuert.Value is { Length: > 0 } gesetzt)
            {
                return gesetzt;
            }

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

    private sealed class Rueckgabe(string? vorher) : IDisposable
    {
        public void Dispose() => Uebersteuert.Value = vorher;
    }
}
