using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
public sealed class EfUnitOfWork(TransferDbContext kontext) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);

        if (kontext.Database.CurrentTransaction is not null)
        {
            return await arbeit(cancellationToken);
        }

        await using var klammer = await kontext.Database.BeginTransactionAsync(cancellationToken);

        var ergebnis = await arbeit(cancellationToken);

        await kontext.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return ergebnis;
    }
}
