using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Assessment.Application.Ports;

namespace WorkerTransfer.Assessment.Infrastructure.Persistence;

/// <summary>A database transaction around a command.</summary>
/// <remarks>
/// It encloses the outbox too: the entry telling the person to read their
/// evaluation comes into being in the same transaction as the evaluation, or
/// not at all (ADR-0025, ADR-0042 §2).
/// </remarks>
public sealed class EfUnitOfWork(AssessmentDbContext context) : IUnitOfWork
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
