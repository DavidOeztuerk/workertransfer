using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Domain.Audit;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>A row of <c>audit_events</c>.</summary>
public sealed class AuditRow
{
    /// <summary>The row's id, time-ordered.</summary>
    public Guid Id { get; set; }

    /// <summary>What happened, as its word.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Whom it concerned.</summary>
    public Guid TargetId { get; set; }

    /// <summary>When.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>The trail in this service's database.</summary>
/// <remarks>
/// <c>Add</c> without <c>SaveChanges</c>: the unit of work commits it together
/// with the erasure, or neither.
/// </remarks>
/// <param name="context">This service's context.</param>
public sealed class EfAuditTrail(GitHubDbContext context) : IAuditTrail
{
    /// <inheritdoc />
    public async Task AppendOnceAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var action = Word(entry.Action);
        var target = entry.Target.Value;

        if (await context.AuditEntries.AnyAsync(
                row => row.TargetId == target && row.Action == action, cancellationToken))
        {
            return;
        }

        context.AuditEntries.Add(new AuditRow
        {
            Id = Guid.CreateVersion7(),
            Action = action,
            TargetId = target,
            OccurredAt = entry.OccurredAt
        });
    }

    private static string Word(AuditAction action) => action switch
    {
        AuditAction.Erased => "erased",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
