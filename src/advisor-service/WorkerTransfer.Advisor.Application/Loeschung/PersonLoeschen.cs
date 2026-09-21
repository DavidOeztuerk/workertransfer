using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Domain.Gespraeche;

namespace WorkerTransfer.Advisor.Application.Erasure;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Das Mandate, die Gespräche und die Vermerke über sie.</summary>
/// <remarks>
/// <para><strong>Alles drei, und in einer Transaktion.</strong> Das Mandate
/// gehört der Person; die Gespräche handeln von ihr und tragen ihre Kennung;
/// die Postausgangszeilen ebenso. Wer nur eines löscht, hinterlässt die anderen
/// — und die Zusage aus ADR-0027 ist dann zur Hälfte eingelöst, ohne dass es
/// jemandem auffällt.</para>
///
/// <para><strong>Kein Aufbewahrungsfall.</strong> Anders als bei einer
/// eingestellten JobApplication oder einem bezahlten Transfer steht hier nichts, was
/// jemand anderem gehört: ein Gespräch ist eine Beziehung, kein Evidence. Die
/// Antwort ist deshalb immer null.</para>
///
/// <para><strong>Die Freigaben fallen nicht hier.</strong> Sie stehen im
/// Ledger, und consent-service ist ein eigener Empfänger derselben Kaskade —
/// dort wird je Fähigkeit eine DELETE-Zeile angehängt (ADR-0027). Sie hier
/// zusätzlich zu widerrufen wäre eine zweite Job für dieselbe Löschung.</para>
/// </remarks>
public sealed class ErasePersonHandler(IConversationStore store)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.DeleteAsync(request.Wer, cancellationToken);
    }
}
