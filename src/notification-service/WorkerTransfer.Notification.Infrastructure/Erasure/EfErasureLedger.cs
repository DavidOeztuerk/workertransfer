using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Infrastructure.Persistence;

namespace WorkerTransfer.Notification.Infrastructure.Erasure;

/// <summary>What an erasure touches in this service.</summary>
/// <remarks>
/// Two statements, both without exception. There is no row here in which
/// somebody merely <em>acted</em> — every one is <em>about</em> the person, and
/// so every one falls. And no retention switch: it was never claimed that
/// anything here is subject to a retention obligation.
/// </remarks>
public sealed class EfErasureLedger(NotificationDbContext context) : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        await context.InboxEntries
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Preferences
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Nothing stays. The receipt says so instead of leaving the origin to
        // guess (ADR-0027 §3).
        return 0;
    }
}
