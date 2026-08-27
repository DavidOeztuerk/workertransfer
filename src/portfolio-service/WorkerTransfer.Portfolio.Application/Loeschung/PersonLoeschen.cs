using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Nachrichten;
using WorkerTransfer.Portfolio.Domain.Ablage;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Loeschung;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Zeilen <em>und</em> Dateien.</summary>
/// <remarks>
/// Beides, und in einer Transaktion. Eine Datei, die nach der Löschung noch auf
/// der Platte liegt, ist genau das stille Scheitern, gegen das ADR-0027 antritt:
/// die Zeile ist weg, die Arbeitsprobe nicht — und niemand sieht es, weil die
/// Oberfläche sie nicht mehr verlinkt.
/// </remarks>
public sealed class PersonLoeschenHandler(IPortfoliospeicher speicher, IAblage ablage)
    : IRequestHandler<PersonLoeschenBefehl, int>
{
    /// <inheritdoc />
    /// <returns>
    /// Wie viel absichtlich stehen blieb. Immer null — ein Portfolio kennt
    /// keinen Aufbewahrungsschalter.
    /// </returns>
    public async Task<int> Handle(
        PersonLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var geblieben = await speicher.LoescheAsync(request.Wer, cancellationToken);

        await ablage.LoescheAllesAsync(request.Wer, cancellationToken);

        return geblieben;
    }
}
