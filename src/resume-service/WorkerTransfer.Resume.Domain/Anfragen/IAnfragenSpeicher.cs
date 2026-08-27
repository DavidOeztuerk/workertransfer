using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Anfragen;

/// <summary>Where the requests are kept.</summary>
public interface IAnfragenSpeicher
{
    /// <summary>One by its id, or <c>null</c>.</summary>
    Task<Anfrage?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The one this company already made about this person, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The pair is unique in the database as well, and that is the rule "ask
    /// once": a refusal is worthless if the same company may ask again — whoever
    /// may ask three times did not get a no, they got a delay. In the handler
    /// alone two simultaneous requests would both pass the check.
    /// </remarks>
    Task<Anfrage?> FindeAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Adds a new one to the open transaction.</summary>
    Task FuegeHinzuAsync(Anfrage anfrage, CancellationToken cancellationToken = default);

    /// <summary>Writes an answered one back.</summary>
    Task SichereAsync(Anfrage anfrage, CancellationToken cancellationToken = default);

    /// <summary>Everything asked about this person — the transparency list.</summary>
    Task<IReadOnlyList<Anfrage>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Everything this company asked.</summary>
    Task<IReadOnlyList<Anfrage>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default);
}
