using Noelia.Core.Identity;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Domain.Sources;

namespace WorkerTransfer.Jobs.Application.Ports;

/// <summary>A service did not answer — no result, a system state (503).</summary>
public sealed class SourceSilent(string reason, Exception? cause = null) : Exception(reason, cause);

/// <summary>What reading a source came to.</summary>
public abstract record SourceRead
{
    private SourceRead()
    {
    }

    /// <summary>The adverts the source lists right now.</summary>
    public sealed record Adverts(IReadOnlyList<ExternalAdvert> Items) : SourceRead;

    /// <summary>It could not be read — a short word, never text from the source.</summary>
    public sealed record Failed(string Outcome) : SourceRead;
}

/// <summary>Reads a source over its official interface (ADR-0052).</summary>
public interface ISourceReader
{
    /// <summary>The adverts, or why there are none.</summary>
    Task<SourceRead> ReadAsync(JobSource source, CancellationToken cancellationToken = default);
}

/// <summary>Reads a company's own feed, on its proven domain.</summary>
public interface ICompanySite
{
    /// <summary>The page's text, or <c>null</c> when it cannot be read.</summary>
    Task<string?> PageAsync(string address, CancellationToken cancellationToken = default);
}

/// <summary>A company's proven domain, from identity-service (ADR-0019).</summary>
public interface ICompanyDomains
{
    /// <summary>The domain, or <c>null</c>: no such company.</summary>
    /// <exception cref="SourceSilent">identity-service did not answer.</exception>
    Task<string?> DomainAsync(TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>The domains the egress boundary lets jobs-service call besides its configuration.</summary>
public interface IKnownDomains
{
    /// <summary>Lets calls to this domain and below it through.</summary>
    void Remember(string domain);
}
