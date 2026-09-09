using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Appends to <c>audit_events</c>.</summary>
/// <remarks>
/// Adds to the change tracker and does not save. Saving here would give each
/// entry a transaction of its own, which is exactly the property the trail must
/// not have: the entry commits with the change it records or it records
/// nothing.
/// </remarks>
public sealed class EfPruefspur(ResumeDbContext kontext) : IPruefspur
{
    /// <inheritdoc />
    public Task AppendAsync(Pruefeintrag eintrag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eintrag);

        kontext.Pruefspur.Add(new PruefZeile
        {
            Id = Guid.CreateVersion7(),
            ActorId = eintrag.Akteur?.Value,
            TenantId = eintrag.Firma?.Value,
            Action = eintrag.Handlung,
            TargetId = eintrag.Betroffene?.Value,
            CorrelationId = eintrag.Korrelation,
            OccurredAt = eintrag.Geschehen.UtcDateTime
        });

        return Task.CompletedTask;
    }
}
