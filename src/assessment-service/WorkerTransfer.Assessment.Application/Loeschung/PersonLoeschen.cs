using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Nachrichten;
using WorkerTransfer.Assessment.Domain.Vorgaenge;

namespace WorkerTransfer.Assessment.Application.Loeschung;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Die Vorgänge samt Aufgaben, Einreichungen und Bewertungen.</summary>
/// <remarks>
/// <para><strong>Kein Aufbewahrungsfall, und ausdrücklich auch nicht für die
/// Bewertung.</strong> Was hier steht, ist kein Beleg, der jemand anderem
/// gehört — anders als eine eingestellte Bewerbung oder ein bezahlter Transfer
/// (ADR-0027 §3). Eine Bewertung, die die Löschung überlebte, wäre das Zeugnis,
/// das ADR-0042 ausschliesst, nur mit besonders schlechtem Zeitpunkt. Die
/// Antwort ist deshalb immer null.</para>
///
/// <para><strong>Die Freigaben fallen nicht hier.</strong> Sie stehen im
/// Ledger, und consent-service ist ein eigener Empfänger derselben Kaskade.
/// Sie hier zusätzlich zu widerrufen wäre eine zweite Stelle für dieselbe
/// Löschung.</para>
/// </remarks>
public sealed class PersonLoeschenHandler(IVorgangsspeicher speicher)
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
