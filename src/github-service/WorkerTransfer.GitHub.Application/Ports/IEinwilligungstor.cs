using Noelia.Core.Identity;

namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet. Kein Ergebnis — ein Systemzustand.</summary>
public sealed class ConsentGateSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);

/// <summary>Der Freigabeschlüssel, an genau einer Job gebaut.</summary>
public static class ConsentKey
{
    /// <summary>
    /// Ein Portfolio aus belegter Arbeit ist eine Sache, die man zeigt.
    /// </summary>
    /// <remarks>
    /// Deshalb <c>:public</c> und nicht empfängerbezogen: anders als beim
    /// MarketStatus sagt „dieser Mensch hat öffentliche Repositories" nichts,
    /// was ihn beim Arbeitgeber in Schwierigkeiten bringt — es steht ohnehin
    /// auf github.com. Das Personendatum ist nicht der Name, sondern die
    /// <em>Verknüpfung</em>: „dieser Plattform-Mensch ist jener GitHub-Name",
    /// und genau die schaltet dieser Schlüssel frei.
    /// </remarks>
    public const string Sichtbarkeit = "github.visibility:public";
}

/// <summary>Fragt den Ledger. Schreibt nie in ihn.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013): ein Revocation muss beim
/// allernächsten Lesezugriff wirken.
/// </remarks>
public interface IConsentGate
{
    /// <summary>Ob die Connection dieser Person gezeigt werden darf.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task<bool> MayBeShownAsync(
        SubjectId subject, CancellationToken cancellationToken = default);
}
