using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet. Kein Ergebnis — ein Systemzustand.</summary>
/// <remarks>
/// Weder zeigen noch leugnen: beides wäre eine Behauptung über die Person, die
/// gerade niemand treffen kann. Der Endpunkt macht daraus 503.
/// </remarks>
public sealed class ConsentGateSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);

/// <summary>Die Freigabeschlüssel, an genau einer Job gebaut.</summary>
public static class ConsentKey
{
    /// <summary>Wer das Profile nicht sehen darf, darf auch nicht nach dem Markt fragen.</summary>
    /// <remarks>
    /// Sonst wäre die ResumeRequest ein Kanal, um zu erfahren, dass es diesen
    /// Menschen hier überhaupt gibt.
    /// </remarks>
    public const string Profile = "profile.visibility:public";

    /// <summary>
    /// Immer empfängerbezogen. Ein <c>:public</c> gibt es hier
    /// <strong>nicht</strong>.
    /// </summary>
    /// <remarks>
    /// Beim Profile ist „für alle Unternehmen" eine sinnvolle Wahl; hier wäre
    /// sie ein Schalter, dessen Folgen niemand überblickt — darunter der eigene
    /// Arbeitgeber, der auf derselben Plattform ist.
    /// </remarks>
    public static string Markt(TenantId company) => $"market.visibility:tenant:{company}";
}

/// <summary>Liest den Ledger und schreibt in ihn.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013). Es gibt hier kein Feld, das
/// eine Antwort halten könnte, und das ist Absicht: ein Revocation muss beim
/// allernächsten Lesezugriff wirken.
/// </remarks>
public interface IConsentGate
{
    /// <summary>Ob das Profile dieser Person überhaupt freigegeben ist.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task<bool> MaySeeProfileAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Ob dieses Unternehmen den MarketStatus gerade sehen darf.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task<bool> MaySeeMarketAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Dieselbe Frage für mehrere Paare, in einem Zug.</summary>
    /// <remarks>
    /// Die Responses kommen <em>in der Reihenfolge der Fragen</em>, eine je
    /// Paar.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">
    /// Der Ledger hat nicht geantwortet, oder anders oft geantwortet als
    /// gefragt wurde.
    /// </exception>
    Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default);

    /// <summary>Hält die Release für dieses eine Unternehmen fest.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task GrantAsync(SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Nimmt sie zurück.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);
}
