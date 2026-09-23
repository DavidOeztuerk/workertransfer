using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>A database transaction around a command.</summary>
public sealed class EfUnitOfWork(ProfileDbContext context) : IUnitOfWork
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
            // half the work silently too early.
            return await work(cancellationToken);
        }

        await using var scope = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return result;
    }
}
