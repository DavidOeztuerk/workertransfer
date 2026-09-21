using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Messages;
using WorkerTransfer.Portfolio.Domain.Documents;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Erasure;

/// <summary>Erases everything this service holds about a person.</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Rows <em>and</em> files.</summary>
/// <remarks>
/// Both, and in one transaction. A file still on the disk after the erasure is
/// exactly the silent failure ADR-0027 stands against: the row is gone, the
/// work sample is not — and nobody sees it, because the interface no longer
/// links to it.
/// </remarks>
public sealed class ErasePersonHandler(IPortfolioStore store, IDocumentStore documents)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>
    /// How much deliberately stayed. Always zero — a portfolio knows no
    /// retention switch.
    /// </returns>
    public async Task<int> Handle(
        ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kept = await store.DeleteAsync(request.Subject, cancellationToken);

        await documents.DeleteAllAsync(request.Subject, cancellationToken);

        return kept;
    }
}
