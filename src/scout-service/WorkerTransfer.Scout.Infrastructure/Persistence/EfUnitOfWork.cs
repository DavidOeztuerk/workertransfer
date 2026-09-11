using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
/// <remarks>
/// Sie umschliesst auch den Postausgang: der Vermerk „dein Profil wurde
/// entdeckt" entsteht in derselben Klammer wie die Antwort, oder gar nicht
/// (ADR-0025).
/// </remarks>
public sealed class EfUnitOfWork(ScoutDbContext kontext) : IUnitOfWork
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
            // die halbe Arbeit früh committen.
            return await arbeit(cancellationToken);
        }

        await using var klammer = await kontext.Database.BeginTransactionAsync(cancellationToken);

        var ergebnis = await arbeit(cancellationToken);

        await kontext.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return ergebnis;
    }
}
