using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Jobs.Application.Ports;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
public sealed class EfUnitOfWork(JobsDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);

        if (context.Database.CurrentTransaction is not null)
        {
            return await arbeit(cancellationToken);
        }

        await using var klammer = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await arbeit(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return result;
    }
}
