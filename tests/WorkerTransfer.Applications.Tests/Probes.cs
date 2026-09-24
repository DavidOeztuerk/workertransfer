using Noelia.Core.Identity;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Tests;

/// <summary>A ledger that writes down instead of asking.</summary>
/// <remarks>
/// It remembers what <em>would</em> have gone out — for this service that is
/// the interesting question, because what it writes to the ledger is the
/// release itself.
/// </remarks>
public sealed class ProbeLedger : IConsentWriter
{
    /// <summary>What was granted, in the order of the calls.</summary>
    public List<string> Granted { get; } = [];

    /// <summary>What was revoked.</summary>
    public List<string> Revoked { get; } = [];

    /// <summary>When set, the ledger is silent.</summary>
    public bool Silent { get; set; }

    /// <inheritdoc />
    public Task GrantAsync(
        SubjectId subject,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new ConsentGateSilent("Probe: the ledger is silent.");
        }

        Granted.AddRange(capabilities);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        SubjectId subject,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new ConsentGateSilent("Probe: the ledger is silent.");
        }

        Revoked.AddRange(capabilities);
        return Task.CompletedTask;
    }
}

/// <summary>A member lookup that answers what the test decides.</summary>
public sealed class ProbeMembers : ICompanyMemberLookup
{
    private readonly Dictionary<TenantId, IReadOnlyList<SubjectId>> _members = [];

    /// <summary>When set, the service is silent.</summary>
    public bool Silent { get; set; }

    /// <summary>Sets the members of a company.</summary>
    public void For(TenantId company, params SubjectId[] members)
    {
        _members[company] = members;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SubjectId>> GetAsync(
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new CompanyLookupSilent("Probe: identity-service is silent.");
        }

        return Task.FromResult(_members.GetValueOrDefault(company, []));
    }
}

/// <summary>A jobs-service that answers what the test decides.</summary>
public sealed class ProbeJobs : IJobLookup
{
    private readonly Dictionary<Guid, PublicJob> _open = [];

    /// <summary>When set, the service is silent.</summary>
    public bool Silent { get; set; }

    /// <summary>Creates a public job and returns its id.</summary>
    public Guid Publish(Guid company)
    {
        var id = Guid.CreateVersion7();
        _open[id] = new PublicJob(id, new TenantId(company), "Developer");
        return id;
    }

    /// <inheritdoc />
    public Task<PublicJob?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new JobLookupSilent("Probe: jobs-service is silent.");
        }

        return Task.FromResult(_open.GetValueOrDefault(job));
    }
}

/// <summary>An AI access the test decides — by default: none.</summary>
public sealed class ProbeAiAccess : IAiAccessLookup
{
    /// <summary>What comes back. Unset: no provider.</summary>
    public AiAccess Value { get; set; } =
        new("none", string.Empty, string.Empty, string.Empty);

    /// <inheritdoc />
    public Task<AiAccess> GetAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Value);
}

/// <summary>A delivery that does nothing — the outbox row is the touchstone.</summary>
public sealed class ProbeDelivery : WorkerTransfer.Outbox.IDelivery
{
    /// <inheritdoc />
    public Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
