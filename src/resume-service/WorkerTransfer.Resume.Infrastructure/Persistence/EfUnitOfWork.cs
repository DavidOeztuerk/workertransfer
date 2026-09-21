using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>One database transaction around one command.</summary>
public sealed class EfUnitOfWork(ResumeDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);

        if (context.Database.CurrentTransaction is not null)
        {
            // Already inside one. Opening a second would either throw or
            // silently commit half the work early.
            return await arbeit(cancellationToken);
        }

        await using var klammer = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await arbeit(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return result;
    }
}
