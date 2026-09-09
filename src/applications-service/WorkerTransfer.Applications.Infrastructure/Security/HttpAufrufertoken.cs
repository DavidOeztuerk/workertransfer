using Microsoft.AspNetCore.Http;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Security;

/// <summary>Liest das Token aus der laufenden Anfrage.</summary>
/// <remarks>
/// Kopf zuerst, dann Cookie — dieselbe Reihenfolge, in der die Dienste eines
/// prüfen. Ein Browser sieht das Token nur als <c>httpOnly</c>-Cookie, ein
/// Dienst-zu-Dienst-Aufrufer schickt den Kopf.
/// </remarks>
public sealed class HttpAufrufertoken(IHttpContextAccessor zugriff) : IAufrufertoken
{
    private static readonly AsyncLocal<string?> Uebersteuert = new();

    /// <summary>Setzt das Token für den Hintergrundauftrag.</summary>
    public static IDisposable Mit(string? wert)
    {
        var vorher = Uebersteuert.Value;
        Uebersteuert.Value = wert;
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

    private sealed class Rueckgabe(string? vorher) : IDisposable
    {
        public void Dispose() => Uebersteuert.Value = vorher;
    }
}
