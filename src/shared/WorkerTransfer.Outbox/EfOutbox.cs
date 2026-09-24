using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace WorkerTransfer.Outbox;

/// <summary>Writes the intent into whatever transaction the caller has open.</summary>
/// <typeparam name="TContext">The service's own context. The table lives in its database.</typeparam>
public sealed class EfOutbox<TContext>(TContext context, TimeProvider clock) : IOutbox
    where TContext : DbContext
{
    /// <inheritdoc />
    public Task<Guid> NoteAsync(
        SubjectId recipient,
        string kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var id = Guid.CreateVersion7();

        context.Set<OutboxRow>().Add(new OutboxRow
        {
            Id = id,
            UserId = recipient.Value,
            Kind = kind,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Attempts = 0,
            DeliveredAt = null,
            LastError = string.Empty
        });

        return Task.FromResult(id);
    }
}
