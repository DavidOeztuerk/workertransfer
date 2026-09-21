using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
public sealed class EfUnitOfWork(ApplicationsDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);

        if (context.Database.CurrentTransaction is not null)
        {
            // Schon in einer. Eine zweite zu öffnen würde entweder werfen oder
            // die halbe Arbeit stillschweigend zu früh committen.
            return await arbeit(cancellationToken);
        }

        await using var klammer = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await arbeit(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return result;
    }
}
