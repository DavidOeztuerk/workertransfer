using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
public sealed class EfUnitOfWork(ProfileDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);

        if (context.Database.CurrentTransaction is not null)
        {
            // Schon in einer drin. Eine zweite zu öffnen würde entweder werfen
            // oder die halbe Arbeit stillschweigend zu früh abschließen.
            return await arbeit(cancellationToken);
        }

        await using var klammer = await context.Database.BeginTransactionAsync(cancellationToken);

        var ergebnis = await arbeit(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return ergebnis;
    }
}
