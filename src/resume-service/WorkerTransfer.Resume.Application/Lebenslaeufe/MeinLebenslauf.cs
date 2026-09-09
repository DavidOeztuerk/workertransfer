using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>One's own résumé, with no ledger in the way.</summary>
/// <remarks>
/// Answers <c>null</c> rather than a refusal when there is none: "has not
/// written one yet" is a state, and the interface shows an empty form for it.
/// A failure here would force it to translate an error back into a normal case.
/// </remarks>
public sealed record MeinLebenslaufAbfrage(SubjectId Wer) : IAbfrage<Lebenslauf?>;

/// <inheritdoc cref="MeinLebenslaufAbfrage" />
public sealed class MeinLebenslaufHandler(ILebenslaufSpeicher speicher)
    : IRequestHandler<MeinLebenslaufAbfrage, Lebenslauf?>
{
    /// <inheritdoc />
    public Task<Lebenslauf?> Handle(
        MeinLebenslaufAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Wer, cancellationToken);
    }
}
