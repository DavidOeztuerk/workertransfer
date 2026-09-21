using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Nachrichten;
using WorkerTransfer.Portfolio.Domain.Ablage;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Erasure;

/// <summary>Löscht alles, was dieser Dienst über eine Person hält.</summary>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Zeilen <em>und</em> Dateien.</summary>
/// <remarks>
/// Beides, und in einer Transaktion. Eine Datei, die nach der Löschung noch auf
/// der Platte liegt, ist genau das stille Scheitern, gegen das ADR-0027 antritt:
/// die Zeile ist weg, die Arbeitsprobe nicht — und niemand sieht es, weil die
/// Oberfläche sie nicht mehr verlinkt.
/// </remarks>
public sealed class ErasePersonHandler(IPortfolioStore store, IDocumentStore ablage)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>
    /// Wie viel absichtlich stehen blieb. Immer null — ein Portfolio kennt
    /// keinen Aufbewahrungsschalter.
    /// </returns>
    public async Task<int> Handle(
        ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var geblieben = await store.DeleteAsync(request.Wer, cancellationToken);

        await ablage.DeleteAllAsync(request.Wer, cancellationToken);

        return geblieben;
    }
}
