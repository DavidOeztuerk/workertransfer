using Girder.Core.Identity;
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
public sealed class PortfolioSichernHandler(IPortfoliospeicher speicher, TimeProvider uhr)
    : IRequestHandler<PortfolioSichernBefehl, Domain.Portfolios.Portfolio>
{
    /// <inheritdoc />
    public async Task<Domain.Portfolios.Portfolio> Handle(
        PortfolioSichernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();
        var vorhanden = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (vorhanden is null)
        {
            var neues = Domain.Portfolios.Portfolio.Lege_an(
                request.Wer, request.Eintraege, jetzt);

            await speicher.SichereAsync(neues, cancellationToken);
            return neues;
        }

        vorhanden.Aendere(request.Eintraege, jetzt);
        await speicher.SichereAsync(vorhanden, cancellationToken);

        return vorhanden;
    }
}
