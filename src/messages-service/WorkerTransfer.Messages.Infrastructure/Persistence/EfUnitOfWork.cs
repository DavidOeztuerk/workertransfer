using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Messages.Application.Ports;

namespace WorkerTransfer.Messages.Infrastructure.Persistence;

/// <summary>A database transaction around a command.</summary>
/// <remarks>
/// It encloses the outbox too: the note telling the other side that a message
/// arrived comes into being in the same transaction as the message, or not at
/// all (ADR-0025, ADR-0047).
/// </remarks>
public sealed class EfUnitOfWork(MessagesDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (context.Database.CurrentTransaction is not null)
        {
            // Already in one. Opening a second would either throw or commit half
            // the work early.
            return await work(cancellationToken);
        }

        await using var scope = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return result;
    }
}
