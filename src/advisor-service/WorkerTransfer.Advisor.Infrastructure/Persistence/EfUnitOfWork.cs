using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Application.Ports;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>A database transaction around a command.</summary>
/// <remarks>
/// It encloses the outbox too: the entry saying that a company opened a
/// conversation comes about in the same transaction as the conversation, or not
/// at all (ADR-0025).
/// </remarks>
public sealed class EfUnitOfWork(AdvisorDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (context.Database.CurrentTransaction is not null)
        {
            // Already inside one. Opening a second would either throw or
            // commit half the work early.
            return await work(cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
