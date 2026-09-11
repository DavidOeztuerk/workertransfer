using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Domain.Suchen;

namespace WorkerTransfer.Scout.Application.Loeschung;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Die gespeicherten Suchen dieses Menschen — und die Vermerke über ihn.</summary>
/// <remarks>
/// <para><strong>Beides, und in einer Transaktion.</strong> Die gespeicherten
/// Suchen gehören dem Menschen, der sie abgelegt hat; die Postausgangszeilen
/// <em>handeln von</em> einem Menschen und tragen seine Kennung. Wer nur die
/// einen löscht, hinterlässt die anderen — und die Zusage aus ADR-0027 ist dann
/// zur Hälfte eingelöst, ohne dass es jemandem auffällt.</para>
///
/// <para>Dieser Dienst kennt keinen Aufbewahrungsfall: hier steht nichts, was
/// jemand anderem gehört. Die Antwort ist deshalb immer null.</para>
/// </remarks>
public sealed class PersonLoeschenHandler(ISuchspeicher speicher)
    : IRequestHandler<PersonLoeschenBefehl, int>
{
    /// <inheritdoc />
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    public Task<int> Handle(PersonLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.LoescheAsync(request.Wer, cancellationToken);
    }
}
