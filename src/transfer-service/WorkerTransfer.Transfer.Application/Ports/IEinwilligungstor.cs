using Girder.Core.Identity;

namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet. Kein Ergebnis — ein Systemzustand.</summary>
/// <remarks>
/// Weder zeigen noch leugnen: beides wäre eine Behauptung über die Person, die
/// gerade niemand treffen kann. Der Endpunkt macht daraus 503.
/// </remarks>
public sealed class EinwilligungSchweigt(string grund, Exception? ursache = null)
    : Exception(grund, ursache);

/// <summary>Die Freigabeschlüssel, an genau einer Stelle gebaut.</summary>
public static class Einwilligungsschluessel
{
    /// <summary>Wer das Profil nicht sehen darf, darf auch nicht nach dem Markt fragen.</summary>
    /// <remarks>
    /// Sonst wäre die Anfrage ein Kanal, um zu erfahren, dass es diesen
    /// Menschen hier überhaupt gibt.
    /// </remarks>
    public const string Profil = "profile.visibility:public";

    /// <summary>
    /// Immer empfängerbezogen. Ein <c>:public</c> gibt es hier
    /// <strong>nicht</strong>.
    /// </summary>
    /// <remarks>
    /// Beim Profil ist „für alle Unternehmen" eine sinnvolle Wahl; hier wäre
    /// sie ein Schalter, dessen Folgen niemand überblickt — darunter der eigene
    /// Arbeitgeber, der auf derselben Plattform ist.
    /// </remarks>
    public static string Markt(TenantId firma) => $"market.visibility:tenant:{firma}";
}

/// <summary>Liest den Ledger und schreibt in ihn.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013). Es gibt hier kein Feld, das
/// eine Antwort halten könnte, und das ist Absicht: ein Widerruf muss beim
/// allernächsten Lesezugriff wirken.
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>Ob das Profil dieser Person überhaupt freigegeben ist.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task<bool> DarfProfilSehenAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Ob dieses Unternehmen den Marktstatus gerade sehen darf.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task<bool> DarfMarktSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Dieselbe Frage für mehrere Paare, in einem Zug.</summary>
    /// <remarks>
    /// Die Antworten kommen <em>in der Reihenfolge der Fragen</em>, eine je
    /// Paar.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">
    /// Der Ledger hat nicht geantwortet, oder anders oft geantwortet als
    /// gefragt wurde.
    /// </exception>
    Task<IReadOnlyList<bool>> DuerfenMarktSehenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default);

    /// <summary>Hält die Freigabe für dieses eine Unternehmen fest.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task ErteileAsync(SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Nimmt sie zurück.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task WiderrufeAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);
}
