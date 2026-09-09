using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Infrastructure.Persistence;

namespace WorkerTransfer.Consent.Infrastructure.Loeschung;

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
public sealed class EfFreitextraeumung(ConsentDbContext kontext) : IFreitextraeumung
{
    /// <inheritdoc />
    public async Task RaeumeAsync(Guid subject, CancellationToken cancellationToken = default)
    {
        // Pending rows first. An UPDATE runs in the database and cannot reach a
        // row that is still only in the change tracker — the closing facts the
        // erasure just appended would keep their free text, which is precisely
        // what this is here to prevent.
        await kontext.SaveChangesAsync(cancellationToken);

        await kontext.ConsentEvents
            .Where(zeile => zeile.SubjectId == subject)
            .ExecuteUpdateAsync(
                zeile => zeile
                    .SetProperty(spalte => spalte.Reason, (string?)null)
                    .SetProperty(spalte => spalte.Metadata, "{}"),
                cancellationToken);

        // The row itself stays: the trail is not cascaded (ADR-0012), it is
        // emptied of what a person wrote.
        await kontext.AuditEvents
            .Where(zeile => zeile.TargetId == subject || zeile.ActorId == subject)
            .ExecuteUpdateAsync(
                zeile => zeile.SetProperty(spalte => spalte.Metadata, "{}"),
                cancellationToken);
    }
}
