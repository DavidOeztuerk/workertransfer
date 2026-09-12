using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Application.Ports;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Eine Datenbanktransaktion um einen Befehl.</summary>
/// <remarks>
/// Sie umschliesst auch den Postausgang: der Vermerk, dass ein Unternehmen ein
/// Gespräch eröffnet hat, entsteht in derselben Klammer wie das Gespräch, oder
/// gar nicht (ADR-0025).
/// </remarks>
public sealed class EfUnitOfWork(AdvisorDbContext kontext) : IUnitOfWork
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
