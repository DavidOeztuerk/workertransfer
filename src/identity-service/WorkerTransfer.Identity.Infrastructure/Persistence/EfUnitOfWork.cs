using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>One database transaction around one command.</summary>
public sealed class EfUnitOfWork(IdentityDbContext context) : IUnitOfWork
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
            // silently commit half the work early.
            return await work(cancellationToken);
        }

        await using var scope = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return result;
    }
}
