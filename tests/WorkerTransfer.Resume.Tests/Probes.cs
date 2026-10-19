using Noelia.Core.Identity;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Tests;

/// <summary>A ledger the test steers, in place of the real one over HTTP.</summary>
/// <remarks>
/// The ledger is a different service and is not running here. What this suite
/// checks is not how it answers but what <em>this</em> service does with the
/// answer — that it asks at all, on every read, and that a silent ledger is
/// neither a yes nor a no.
/// </remarks>
public sealed class ProbeGate : IConsentGate
{
    /// <summary>Who has released their profile at all.</summary>
    public HashSet<Guid> ProfileReleased { get; } = [];

    /// <summary>Which (person, company) pairs may read a résumé right now.</summary>
    public HashSet<(Guid Subject, Guid Company)> ResumeReleased { get; } = [];

    /// <summary>Which pairs have released the documents.</summary>
    /// <remarks>
    /// A set of its OWN and not the same one as for the résumé: the probe must
    /// be able to represent the case in which the career history is released
    /// and the references are not. Two capabilities sharing one probe are
    /// always set together in the test — and then nobody checks the separation.
    /// </remarks>
    public HashSet<(Guid Subject, Guid Company)> DocumentsReleased { get; } = [];

    /// <summary>How often the gate was asked. The read path must ask every time.</summary>
    public int Questions { get; private set; }

    /// <summary>When true, the ledger says nothing at all.</summary>
    public bool Silent { get; set; }

    public Task<bool> MaySeeProfileAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        Questions++;
        return Silent
            ? throw new ConsentGateSilent("the ledger does not answer in the test")
            : Task.FromResult(ProfileReleased.Contains(subject.Value));
    }

    public Task<bool> MayReadResumeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Questions++;
        return Silent
            ? throw new ConsentGateSilent("the ledger does not answer in the test")
            : Task.FromResult(ResumeReleased.Contains((subject.Value, company.Value)));
    }

    /// <inheritdoc />
    public Task<bool> MaySeeDocumentsAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Questions++;
        return Silent
            ? throw new ConsentGateSilent("the ledger does not answer in the test")
            : Task.FromResult(DocumentsReleased.Contains((subject.Value, company.Value)));
    }

    /// <summary>
    /// The service does not decide the release itself — it asks the ledger to
    /// record it. Here that write is what the answer to a request turns into.
    /// </summary>
    public Task GrantAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        ResumeReleased.Add((subject.Value, company.Value));
        return Task.CompletedTask;
    }

    /// <inheritdoc cref="GrantAsync" />
    public Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        ResumeReleased.Remove((subject.Value, company.Value));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<bool>> MayEachReadResumeAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        Questions++;
        return Silent
            ? throw new ConsentGateSilent("the ledger does not answer in the test")
            : Task.FromResult<IReadOnlyList<bool>>(
                [.. pairs.Select(pair =>
                    ResumeReleased.Contains((pair.Subject.Value, pair.Company.Value)))]);
    }
}

/// <summary>A delivery that records instead of sending.</summary>
public sealed class ProbeDelivery : IDelivery
{
    public List<(Guid Recipient, string Kind)> Delivered { get; } = [];

    public Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        Delivered.Add((recipient.Value, kind));
        return Task.CompletedTask;
    }
}
