using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Infrastructure.Persistence;

namespace WorkerTransfer.Consent.Infrastructure.Erasure;

/// <summary>Clears the free text out of one person's rows, and nothing else.</summary>
/// <remarks>
/// The one place in this service that writes over an existing row. It is
/// reachable from the erasure and from nowhere else, and it touches exactly two
/// columns in each of two tables.
/// <para>
/// <c>metadata</c> goes even where nothing writes it today: the erasure decides
/// the <em>shape</em>, not the state of play. The allowlist permits <c>ip</c>
/// and <c>user_agent</c>, and the day something starts writing them, this must
/// already have covered them.
/// </para>
/// </remarks>
public sealed class EfFreitextraeumung(ConsentDbContext context) : IFreitextraeumung
{
    /// <inheritdoc />
    public async Task ClearAsync(Guid subject, CancellationToken cancellationToken = default)
    {
        // Pending rows first. An UPDATE runs in the database and cannot reach a
        // row that is still only in the change tracker — the closing facts the
        // erasure just appended would keep their free text, which is precisely
        // what this is here to prevent.
        await context.SaveChangesAsync(cancellationToken);

        await context.ConsentEvents
            .Where(row => row.SubjectId == subject)
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(column => column.Reason, (string?)null)
                    .SetProperty(column => column.Metadata, "{}"),
                cancellationToken);

        // The row itself stays: the trail is not cascaded (ADR-0012), it is
        // emptied of what a person wrote.
        await context.AuditEvents
            .Where(row => row.TargetId == subject || row.ActorId == subject)
            .ExecuteUpdateAsync(
                row => row.SetProperty(column => column.Metadata, "{}"),
                cancellationToken);
    }
}
