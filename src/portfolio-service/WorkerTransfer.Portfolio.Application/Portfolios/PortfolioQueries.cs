using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Messages;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Portfolios;

/// <summary>One's own portfolio.</summary>
/// <remarks>
/// Without asking the ledger. The release governs who sees it from outside,
/// not whether somebody may read their own.
/// </remarks>
public sealed record MyPortfolioQuery(SubjectId Subject)
    : IQueryMessage<Domain.Portfolios.Portfolio?>;

/// <inheritdoc cref="MyPortfolioQuery" />
public sealed class MyPortfolioHandler(IPortfolioStore store)
    : IRequestHandler<MyPortfolioQuery, Domain.Portfolios.Portfolio?>
{
    /// <inheritdoc />
    public Task<Domain.Portfolios.Portfolio?> Handle(
        MyPortfolioQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Subject, cancellationToken);
    }
}

/// <summary>Another person's portfolio.</summary>
/// <param name="Subject">Who is being asked about.</param>
public sealed record OtherPortfolioQuery(SubjectId Subject)
    : IQueryMessage<Domain.Portfolios.Portfolio?>;

/// <summary>Asks the ledger first, the store second.</summary>
/// <remarks>
/// In that order and not the other way round: had it read first and asked
/// afterwards, the content of a hidden portfolio would lie in this process's
/// memory, and the next fault in the response would hand it out.
/// <para>
/// <c>null</c> means <em>hidden or non-existent</em>, and the Api layer must
/// answer both the same way: a difference would say whether this person has
/// shown anything here.
/// </para>
/// </remarks>
public sealed class OtherPortfolioHandler(
    IPortfolioStore store, IConsentGate gate)
    : IRequestHandler<OtherPortfolioQuery, Domain.Portfolios.Portfolio?>
{
    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    public async Task<Domain.Portfolios.Portfolio?> Handle(
        OtherPortfolioQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await gate.MaySeeAsync(request.Subject, cancellationToken)
            ? await store.GetAsync(request.Subject, cancellationToken)
            : null;
    }
}
