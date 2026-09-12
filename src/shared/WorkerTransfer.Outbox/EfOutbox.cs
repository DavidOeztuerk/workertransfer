using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace WorkerTransfer.Outbox;

/// <summary>Writes the intent into whatever transaction the caller has open.</summary>
/// <typeparam name="TKontext">The service's own context. The table lives in its database.</typeparam>
public sealed class EfOutbox<TKontext>(TKontext kontext, TimeProvider uhr) : IOutbox
    where TKontext : DbContext
{
    /// <inheritdoc />
    public Task<Guid> VermerkeAsync(
        SubjectId empfaenger,
        string art,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(art);

        var id = Guid.CreateVersion7();

        kontext.Set<OutboxZeile>().Add(new OutboxZeile
        {
            Id = id,
            UserId = empfaenger.Value,
            Kind = art,
            CreatedAt = uhr.GetUtcNow().UtcDateTime,
            Attempts = 0,
            DeliveredAt = null,
            LastError = string.Empty
        });

        return Task.FromResult(id);
    }
}
