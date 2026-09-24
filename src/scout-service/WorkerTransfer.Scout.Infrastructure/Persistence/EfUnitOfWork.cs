using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>A database transaction around a command.</summary>
/// <remarks>
/// It encloses the outbox too: the note "your profile was discovered" arises in
/// the same scope as the answer, or not at all (ADR-0025).
/// </remarks>
public sealed class EfUnitOfWork(ScoutDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (context.Database.CurrentTransaction is not null)
        {
            // Already inside one. Opening a second would either throw or commit
            // half of the work early.
            return await work(cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
