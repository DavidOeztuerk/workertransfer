using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Erasure;

/// <summary>What an erasure really touches in this service.</summary>
/// <remarks>
/// Two statements, and the first is the one that matters: by default
/// <em>all</em> of this person's applications fall, the hired ones and the paid
/// cases behind them included. Only the flipped switch exempts exactly one row
/// class.
/// </remarks>
public sealed class EfErasureLedger(ApplicationsDbContext context) : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject,
        bool keepHired,
        CancellationToken cancellationToken = default)
    {
        var hired = ApplicationStates.Word(ApplicationState.Hired);
        var theirs = context.Applications.Where(row => row.SubjectId == subject.Value);

        var kept = keepHired
            ? await theirs.CountAsync(row => row.Status == hired, cancellationToken)
            : 0;

        // Written out instead of as a ternary in the expression: this is the
        // line that decides whether a hired application falls. Whoever skims it
        // should see what it does — not have to look up operator precedence.
        if (keepHired)
        {
            await theirs
                .Where(row => row.Status != hired)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await theirs.ExecuteDeleteAsync(cancellationToken);
        }

        // A pending notification to an account that no longer exists.
        await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Suspended is not skipped: the origin should learn what stayed rather
        // than guess (ADR-0027 §3.4).
        return kept;
    }
}
