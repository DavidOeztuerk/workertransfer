using Noelia.Core.Identity;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>A ledger that writes down instead of asking.</summary>
public sealed class ProbeLedger : IConsentGate
{
    /// <summary>Whose profile counts as released. Empty means: nobody's.</summary>
    public HashSet<Guid> ProfileReleased { get; } = [];

    /// <summary>Which (person, company) may see the market status.</summary>
    public HashSet<(Guid Subject, Guid Company)> MarketReleased { get; } = [];

    /// <summary>When set, the ledger stays silent.</summary>
    public bool Silent { get; set; }

    /// <summary>What was granted, in the order of the calls.</summary>
    public List<(Guid Subject, Guid Company)> Granted { get; } = [];

    /// <summary>What was revoked.</summary>
    public List<(Guid Subject, Guid Company)> Revoked { get; } = [];

    /// <inheritdoc />
    public Task<bool> MaySeeProfileAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        return Task.FromResult(ProfileReleased.Contains(subject.Value));
    }

    /// <inheritdoc />
    public Task<bool> MaySeeMarketAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        return Task.FromResult(MarketReleased.Contains((subject.Value, company.Value)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        ArgumentNullException.ThrowIfNull(pairs);

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. pairs.Select(pair => MarketReleased.Contains((pair.Subject.Value, pair.Company.Value)))]);
    }

    /// <inheritdoc />
    public Task GrantAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        Granted.Add((subject.Value, company.Value));
        MarketReleased.Add((subject.Value, company.Value));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        Revoked.Add((subject.Value, company.Value));
        MarketReleased.Remove((subject.Value, company.Value));
        return Task.CompletedTask;
    }

    private void ThrowIfSilent()
    {
        if (Silent)
        {
            throw new ConsentGateSilent("Probe: the ledger is silent.");
        }
    }
}

/// <summary>A delivery that does nothing — the outbox row is the touchstone.</summary>
public sealed class ProbeDelivery : IDelivery
{
    /// <inheritdoc />
    public Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
