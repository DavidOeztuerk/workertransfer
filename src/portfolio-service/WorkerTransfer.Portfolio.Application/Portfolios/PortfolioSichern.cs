using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Nachrichten;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Portfolios;

/// <summary>Schreibt das eigene Portfolio.</summary>
/// <remarks>
/// <c>Wer</c> kommt aus dem geprüften Token und nie aus dem Rumpf: eine
/// <c>SubjectId</c> auf der Leitung wäre ein Weg, in ein fremdes zu schreiben.
/// </remarks>
public sealed record PortfolioSichernBefehl(SubjectId Wer, IReadOnlyList<Eintrag> Eintraege)
    : IBefehl<Domain.Portfolios.Portfolio>;

/// <inheritdoc cref="PortfolioSichernBefehl" />
public sealed class PortfolioSichernHandler(IPortfolioStore store, TimeProvider uhr)
    : IRequestHandler<PortfolioSichernBefehl, Domain.Portfolios.Portfolio>
{
    /// <inheritdoc />
    public async Task<Domain.Portfolios.Portfolio> Handle(
        PortfolioSichernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = uhr.GetUtcNow();
        var vorhanden = await store.GetAsync(request.Wer, cancellationToken);

        if (vorhanden is null)
        {
            var neues = Domain.Portfolios.Portfolio.Lege_an(
                request.Wer, request.Eintraege, now);

            await store.SaveAsync(neues, cancellationToken);
            return neues;
        }

        vorhanden.Aendere(request.Eintraege, now);
        await store.SaveAsync(vorhanden, cancellationToken);

        return vorhanden;
    }
}
