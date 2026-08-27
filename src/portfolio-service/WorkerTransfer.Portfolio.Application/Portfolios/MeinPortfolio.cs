using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Nachrichten;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Portfolios;

/// <summary>Das eigene Portfolio.</summary>
/// <remarks>
/// Ohne Ledgerfrage. Die Freigabe regelt, wer es von außen sieht, nicht ob
/// jemand sein eigenes lesen darf.
/// </remarks>
public sealed record MeinPortfolioAbfrage(SubjectId Wer) : IAbfrage<Domain.Portfolios.Portfolio?>;

/// <inheritdoc cref="MeinPortfolioAbfrage" />
public sealed class MeinPortfolioHandler(IPortfoliospeicher speicher)
    : IRequestHandler<MeinPortfolioAbfrage, Domain.Portfolios.Portfolio?>
{
    /// <inheritdoc />
    public Task<Domain.Portfolios.Portfolio?> Handle(
        MeinPortfolioAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Wer, cancellationToken);
    }
}

/// <summary>Das Portfolio einer anderen Person.</summary>
/// <param name="Wer">Nach wem gefragt wird.</param>
public sealed record FremdesPortfolioAbfrage(SubjectId Wer)
    : IAbfrage<Domain.Portfolios.Portfolio?>;

/// <summary>Fragt erst den Ledger, dann die Ablage.</summary>
/// <remarks>
/// In dieser Reihenfolge und nicht umgekehrt: wäre zuerst gelesen und dann
/// gefragt worden, läge der Inhalt eines verborgenen Portfolios im Speicher
/// dieses Prozesses, und der nächste Fehler in der Antwort gäbe ihn heraus.
/// <para>
/// <c>null</c> heißt <em>verborgen oder nicht vorhanden</em>, und die
/// Api-Schicht muss beides gleich beantworten: ein Unterschied sagte, ob dieser
/// Mensch hier etwas gezeigt hat.
/// </para>
/// </remarks>
public sealed class FremdesPortfolioHandler(
    IPortfoliospeicher speicher, IEinwilligungstor tor)
    : IRequestHandler<FremdesPortfolioAbfrage, Domain.Portfolios.Portfolio?>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<Domain.Portfolios.Portfolio?> Handle(
        FremdesPortfolioAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await tor.DarfSehenAsync(request.Wer, cancellationToken)
            ? await speicher.HoleAsync(request.Wer, cancellationToken)
            : null;
    }
}
