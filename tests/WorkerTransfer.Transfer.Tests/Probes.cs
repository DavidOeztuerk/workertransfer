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

    /// <summary>Which (person, employer) routes stand open (ADR-0050).</summary>
    public HashSet<(Guid Subject, Guid Employer)> RoutesOpen { get; } = [];

    /// <summary>How often the route was asked — a cache in front would show as fewer.</summary>
    public int RouteQuestions { get; private set; }

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

    /// <inheritdoc />
    public Task<bool> EmployerRouteOpenAsync(
        SubjectId subject, TenantId employer, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        RouteQuestions++;
        return Task.FromResult(RoutesOpen.Contains((subject.Value, employer.Value)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> EmployerRoutesOpenAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Employer)> pairs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        ArgumentNullException.ThrowIfNull(pairs);
        RouteQuestions++;

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. pairs.Select(pair => RoutesOpen.Contains((pair.Subject.Value, pair.Employer.Value)))]);
    }

    /// <inheritdoc />
    public Task OpenEmployerRouteAsync(
        SubjectId subject, TenantId employer, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        RoutesOpen.Add((subject.Value, employer.Value));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseEmployerRouteAsync(
        SubjectId subject, TenantId employer, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        RoutesOpen.Remove((subject.Value, employer.Value));
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

/// <summary>identity-service's internal doors, answered from memory.</summary>
/// <remarks>It REMEMBERS whose name it was asked for — the employer may learn it, nobody else.</remarks>
public sealed class ProbeIdentity : IIdentityLookup
{
    /// <summary>The companies that exist.</summary>
    public HashSet<Guid> Companies { get; } = [];

    /// <summary>Civil names.</summary>
    public Dictionary<Guid, string> Names { get; } = [];

    /// <summary>Members per company.</summary>
    public Dictionary<Guid, List<Guid>> Members { get; } = [];

    /// <summary>Whose names were asked for.</summary>
    public List<Guid> NamesAsked { get; } = [];

    /// <summary>When set, identity-service is out.</summary>
    public bool Silent { get; set; }

    /// <inheritdoc />
    public Task<bool> CompanyExistsAsync(TenantId company, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        return Task.FromResult(Companies.Contains(company.Value));
    }

    /// <inheritdoc />
    public Task<string?> PersonNameAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        NamesAsked.Add(subject.Value);
        return Task.FromResult(Names.TryGetValue(subject.Value, out var name) ? name : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SubjectId>> MembersAsync(TenantId company, CancellationToken cancellationToken = default)
    {
        ThrowIfSilent();
        return Task.FromResult<IReadOnlyList<SubjectId>>(
            Members.TryGetValue(company.Value, out var members) ? [.. members.Select(id => new SubjectId(id))] : []);
    }

    private void ThrowIfSilent()
    {
        if (Silent)
        {
            throw new IdentitySilent("Probe: identity-service is silent.");
        }
    }
}
