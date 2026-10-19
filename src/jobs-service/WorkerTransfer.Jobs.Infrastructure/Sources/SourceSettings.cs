namespace WorkerTransfer.Jobs.Infrastructure.Sources;

/// <summary>Where the ATS answer, and how a fetch is paced (ADR-0052).</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Sources__Greenhouse</c>,
/// <c>Sources__Lever</c>, <c>Sources__Personio</c>, …); renaming one alone
/// leaves its value on the default, silently. The two addresses stand in
/// compose too, because the egress boundary derives its allowed hosts from the
/// configuration; Personio's per-company hosts are let through by
/// <see cref="CompanyDomainEgress"/>.
/// </remarks>
public sealed class SourceSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Sources";

    /// <summary>Greenhouse's job-board API.</summary>
    public string Greenhouse { get; set; } = "https://boards-api.greenhouse.io";

    /// <summary>Lever's postings API.</summary>
    public string Lever { get; set; } = "https://api.lever.co";

    /// <summary>The domain under which each Personio company has its host.</summary>
    public string Personio { get; set; } = "jobs.personio.de";

    /// <summary>How long one read may take.</summary>
    /// <remarks>A board of a few hundred adverts is several megabytes; measured at 2.4 MB (Greenhouse) and 6 MB (Lever).</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The most a board's answer may weigh.</summary>
    public long MaxBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>How long a company's own page may take.</summary>
    public TimeSpan PageTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The most a company's own page or feed may weigh.</summary>
    public long PageMaxBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>How often the daily run looks for due sources; zero switches it off.</summary>
    public TimeSpan Every { get; set; } = TimeSpan.FromHours(1);

    /// <summary>After how long a source is due again.</summary>
    public TimeSpan Due { get; set; } = TimeSpan.FromHours(24);
}
