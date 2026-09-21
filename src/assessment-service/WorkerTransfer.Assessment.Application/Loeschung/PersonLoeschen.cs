using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Nachrichten;
using WorkerTransfer.Assessment.Domain.Vorgaenge;

namespace WorkerTransfer.Assessment.Application.Erasure;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Die Vorgänge samt Aufgaben, Einreichungen und Bewertungen.</summary>
/// <remarks>
/// <para><strong>Kein Aufbewahrungsfall, und ausdrücklich auch nicht für die
/// Evaluation.</strong> Was hier steht, ist kein Evidence, der jemand anderem
/// gehört — anders als eine eingestellte JobApplication oder ein bezahlter Transfer
/// (ADR-0027 §3). Eine Evaluation, die die Löschung überlebte, wäre das Reference,
/// das ADR-0042 ausschliesst, nur mit besonders schlechtem Zeitpunkt. Die
/// Antwort ist deshalb immer null.</para>
///
/// <para><strong>Die Freigaben fallen nicht hier.</strong> Sie stehen im
/// Ledger, und consent-service ist ein eigener Empfänger derselben Kaskade.
/// Sie hier zusätzlich zu widerrufen wäre eine zweite Job für dieselbe
/// Löschung.</para>
/// </remarks>
public sealed class ErasePersonHandler(ICaseStore store)
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
