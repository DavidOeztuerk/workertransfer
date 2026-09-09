using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>Where a résumé is kept.</summary>
/// <remarks>
/// No listing and no search. There is no query over résumés in this system, and
/// an interface that offered one would be the first step towards a candidate
/// list — which is exactly what ADR-0022 removed.
/// </remarks>
public interface ILebenslaufSpeicher
{
    /// <summary><c>null</c> means nobody has written one. That is a state, not an error.</summary>
    Task<Lebenslauf?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Writes it. Joins whatever transaction is open.</summary>
    Task SichereAsync(Lebenslauf lebenslauf, CancellationToken cancellationToken = default);
}
