using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
public sealed class EfUnitOfWork(ApplicationsDbContext kontext) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);

        if (kontext.Database.CurrentTransaction is not null)
        {
            // Schon in einer. Eine zweite zu öffnen würde entweder werfen oder
            // die halbe Arbeit stillschweigend zu früh committen.
            return await arbeit(cancellationToken);
        }

        await using var klammer = await kontext.Database.BeginTransactionAsync(cancellationToken);

        var ergebnis = await arbeit(cancellationToken);

        await kontext.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return ergebnis;
    }
}
