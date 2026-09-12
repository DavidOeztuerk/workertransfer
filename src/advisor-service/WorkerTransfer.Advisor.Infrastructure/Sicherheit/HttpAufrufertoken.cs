using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.Advisor.Infrastructure.Sicherheit;

/// <summary>Das Token der laufenden Anfrage.</summary>
/// <remarks>
/// Kopf zuerst, dann Cookie — dieselbe Reihenfolge, in der die Dienste eines
/// prüfen. Ein Browser sieht das Token nur als <c>httpOnly</c>-Cookie, ein
/// Dienst-zu-Dienst-Aufrufer schickt den Kopf.
/// <para>
/// Dass dieser Dienst den Ledger mit <em>diesem</em> Token befragt und
/// beschreibt, ist keine Bequemlichkeit: der Ledger verwaltet sich strikt
/// selbst, also kann eine Stufenfreigabe nur gelingen, wenn die Person sie
/// selbst auslöst.
/// </para>
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
            var anfrage = zugriff.HttpContext?.Request;

            if (anfrage is null)
            {
                return null;
            }

            var kopf = anfrage.Headers.Authorization.ToString();

            if (kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return kopf["Bearer ".Length..].Trim();
            }

            return anfrage.Cookies.TryGetValue("access", out var ausCookie)
                   && !string.IsNullOrEmpty(ausCookie)
                ? ausCookie
                : null;
        }
    }
}
