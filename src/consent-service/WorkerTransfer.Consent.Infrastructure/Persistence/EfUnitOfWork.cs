using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Consent.Application.Ports;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>One database transaction around one command.</summary>
public sealed class EfUnitOfWork(ConsentDbContext context) : IUnitOfWork
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

        await using var klammer = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return result;
    }
}
