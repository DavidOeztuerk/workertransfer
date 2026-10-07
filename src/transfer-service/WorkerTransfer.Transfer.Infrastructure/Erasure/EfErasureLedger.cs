using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Erasure;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Cases;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

namespace WorkerTransfer.Transfer.Infrastructure.Erasure;

/// <summary>What an erasure really touches in this service.</summary>
/// <remarks>
/// Five statements, and the pairing of the middle two is the whole design: a
/// row <em>about</em> the person falls, a row in which the person merely
/// <em>acted</em> stays without their name. Without that distinction a
/// recruiter giving up their private account would erase cases belonging to
/// their employer that are about somebody else entirely.
/// </remarks>
public sealed class EfErasureLedger(TransferDbContext context) : IErasureLedger
{
    private static readonly string[] Completed =
        [.. Retention.Completed.Select(TransferStates.Word)];

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject,
        bool keepPaid,
        CancellationToken cancellationToken = default)
    {
        // The market status — the most dangerous statement in the system, note
        // and all.
        await context.MarketStatus
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // The notice period — when somebody could be gone (H2).
        await context.NoticePeriods
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // What was said ABOUT the person falls: the row IS the statement
        // "company X asked about this human being".
        await context.Requests
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // What the person did FOR their company stays — without their name.
        // The request belongs to the company and is about a third party.
        await context.Requests
            .Where(row => row.RequestedBy == subject.Value)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.RequestedBy, (Guid?)null),
                cancellationToken);

        var theirs = context.Cases.Where(row => row.SubjectId == subject.Value);

        var kept = keepPaid
            ? await theirs.CountAsync(Paid(), cancellationToken)
            : 0;

        // Written out rather than as a ternary in the expression: this is the
        // line that decides whether a paid transfer falls. Whoever skims it
        // should see what it does — not have to look up operator precedence.
        if (keepPaid)
        {
            await theirs
                .Where(row => !(Completed.Contains(row.Status)
                                  && row.OfferFeeCents != null))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await theirs.ExecuteDeleteAsync(cancellationToken);
        }

        // A pending note to an account that no longer exists.
        await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Suspended is not skipped: the origin should learn what stayed
        // instead of guessing (ADR-0027 §3.4).
        return kept;
    }

    /// <summary>A completed deal <em>with</em> a fee.</summary>
    /// <remarks>
    /// Without a fee no trade has come into being that anything could hang on —
    /// and a conversation is no contract.
    /// </remarks>
    private static System.Linq.Expressions.Expression<Func<CaseRow, bool>> Paid() =>
        row => Completed.Contains(row.Status) && row.OfferFeeCents != null;
}
