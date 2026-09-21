using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Messages;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Portfolios;

/// <summary>Writes one's own portfolio.</summary>
/// <remarks>
/// <c>Subject</c> comes out of the verified token and never out of the body: a
/// <c>SubjectId</c> on the wire would be a way into somebody else's.
/// </remarks>
public sealed record SavePortfolioCommand(SubjectId Subject, IReadOnlyList<Entry> Entries)
    : ICommandMessage<Domain.Portfolios.Portfolio>;

/// <inheritdoc cref="SavePortfolioCommand" />
public sealed class SavePortfolioHandler(IPortfolioStore store, TimeProvider clock)
    : IRequestHandler<SavePortfolioCommand, Domain.Portfolios.Portfolio>
{
    /// <inheritdoc />
    public async Task<Domain.Portfolios.Portfolio> Handle(
        SavePortfolioCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        var existing = await store.GetAsync(request.Subject, cancellationToken);

        if (existing is null)
        {
            var created = Domain.Portfolios.Portfolio.Create(
                request.Subject, request.Entries, now);

            await store.SaveAsync(created, cancellationToken);
            return created;
        }

        existing.Change(request.Entries, now);
        await store.SaveAsync(existing, cancellationToken);

        return existing;
    }
}
