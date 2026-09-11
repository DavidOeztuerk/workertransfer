using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Domain.Gespraeche;

namespace WorkerTransfer.Advisor.Application.Loeschung;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Das Mandat, die Gespräche und die Vermerke über sie.</summary>
/// <remarks>
/// <para><strong>Alles drei, und in einer Transaktion.</strong> Das Mandat
/// gehört der Person; die Gespräche handeln von ihr und tragen ihre Kennung;
/// die Postausgangszeilen ebenso. Wer nur eines löscht, hinterlässt die anderen
/// — und die Zusage aus ADR-0027 ist dann zur Hälfte eingelöst, ohne dass es
/// jemandem auffällt.</para>
///
/// <para><strong>Kein Aufbewahrungsfall.</strong> Anders als bei einer
/// eingestellten Bewerbung oder einem bezahlten Transfer steht hier nichts, was
/// jemand anderem gehört: ein Gespräch ist eine Beziehung, kein Beleg. Die
/// Antwort ist deshalb immer null.</para>
///
/// <para><strong>Die Freigaben fallen nicht hier.</strong> Sie stehen im
/// Ledger, und consent-service ist ein eigener Empfänger derselben Kaskade —
/// dort wird je Fähigkeit eine DELETE-Zeile angehängt (ADR-0027). Sie hier
/// zusätzlich zu widerrufen wäre eine zweite Stelle für dieselbe Löschung.</para>
/// </remarks>
public sealed class PersonLoeschenHandler(IGespraechsspeicher speicher)
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
