using System.Text.RegularExpressions;
using Noelia.Core.Identity;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Domain.Sources;

/// <summary>Where a company's adverts come from (ADR-0052).</summary>
public enum SourceKind
{
    /// <summary>Greenhouse's public job board.</summary>
    Greenhouse,

    /// <summary>Lever's public postings.</summary>
    Lever,

    /// <summary>Personio's XML feed.</summary>
    Personio,

    /// <summary>The company's own feed: a JSON list of schema.org JobPosting on its own domain.</summary>
    Feed
}

/// <summary>Whether fetched adverts wait for a human or go out at once.</summary>
public enum PublishMode
{
    /// <summary>As drafts; an admin publishes each.</summary>
    Draft,

    /// <summary>Published at once — in the company's name.</summary>
    Direct
}

/// <summary>The words kinds and modes travel under.</summary>
public static partial class SourceWords
{
    /// <summary>The word for a kind.</summary>
    public static string Word(SourceKind kind) => kind switch
    {
        SourceKind.Greenhouse => "greenhouse",
        SourceKind.Lever => "lever",
        SourceKind.Personio => "personio",
        SourceKind.Feed => "feed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The kind for a word, or <c>null</c>.</summary>
    public static SourceKind? ReadKind(string? word) => word switch
    {
        "greenhouse" => SourceKind.Greenhouse,
        "lever" => SourceKind.Lever,
        "personio" => SourceKind.Personio,
        "feed" => SourceKind.Feed,
        _ => null
    };

    /// <summary>The word for a mode.</summary>
    public static string Word(PublishMode mode) => mode == PublishMode.Direct ? "direct" : "draft";

    /// <summary>The mode for a word, or <c>null</c>.</summary>
    public static PublishMode? ReadMode(string? word) => word switch
    {
        "draft" => PublishMode.Draft,
        "direct" => PublishMode.Direct,
        _ => null
    };
}

/// <summary>Something about a source is wrong.</summary>
public sealed class SourceError(string reason) : InputError(reason);

/// <summary>
/// A company's source of adverts — an ATS board or its own feed (ADR-0052).
/// </summary>
/// <remarks>
/// <para><strong>The binding is the point, and it runs from the board to the
/// company.</strong> A board name is something anybody can type, and every
/// board is publicly readable — so naming one proves nothing, and neither does
/// a link to it from the company's own page or DNS: a company controls those,
/// not the board. What proves it is a change only the board's owner can make:
/// the platform hands out a code, the company writes it into one advert at its
/// ATS, and the next read finds it there. Until then nothing is imported.</para>
///
/// <para>A feed is proven by where it lies: on the company's proven domain
/// (ADR-0019), copied in at setup — which is also what the egress boundary
/// learns.</para>
/// </remarks>
public sealed partial class JobSource
{
    /// <summary>The longest key or address.</summary>
    public const int MaxKeyLength = 300;

    /// <summary>What every code starts with — so a company finds it again in its advert.</summary>
    public const string CodePrefix = "wt-";

    private const string CodeLetters = "abcdefghjkmnpqrstuvwxyz23456789";

    private JobSource(
        Guid id, TenantId company, SourceKind kind, string key, string domain, string code,
        PublishMode mode, DateTimeOffset createdAt, DateTimeOffset? provenAt, DateTimeOffset? fetchedAt, string outcome)
    {
        Id = id;
        Company = company;
        Kind = kind;
        Key = key;
        Domain = domain;
        Code = code;
        Mode = mode;
        CreatedAt = createdAt;
        ProvenAt = provenAt;
        FetchedAt = fetchedAt;
        Outcome = outcome;
    }

    /// <summary>Which source.</summary>
    public Guid Id { get; }

    /// <summary>Whose.</summary>
    public TenantId Company { get; }

    /// <summary>What kind.</summary>
    public SourceKind Kind { get; }

    /// <summary>The board token, site or Personio name — or the feed's address.</summary>
    public string Key { get; }

    /// <summary>The company's proven domain, for a feed; empty for a board.</summary>
    public string Domain { get; }

    /// <summary>What the company writes into one advert at its ATS; empty for a feed.</summary>
    public string Code { get; }

    /// <summary>Draft or direct.</summary>
    public PublishMode Mode { get; private set; }

    /// <summary>When it was set up.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the board was found to carry the code — <c>null</c>: not yet, and nothing is imported.</summary>
    public DateTimeOffset? ProvenAt { get; private set; }

    /// <summary>When it was last fetched, or <c>null</c>: never.</summary>
    public DateTimeOffset? FetchedAt { get; private set; }

    /// <summary>What the last fetch came to — a short word, never text from the source.</summary>
    public string Outcome { get; private set; }

    /// <summary>Whether adverts may come from it.</summary>
    public bool IsProven => ProvenAt is not null;

    /// <summary>Sets a board up — unproven, with a fresh code.</summary>
    public static JobSource Board(TenantId company, SourceKind kind, string? key, PublishMode mode, DateTimeOffset now)
    {
        if (kind == SourceKind.Feed)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A feed is set up with Feed().");
        }

        var cleanKey = CleanKey(key).ToLowerInvariant();

        if (!BoardKey().IsMatch(cleanKey))
        {
            // web/src/features/company/api/sources.ts matches this sentence.
            throw new SourceError("The board name may hold only letters, digits and dashes");
        }

        return new JobSource(
            Guid.CreateVersion7(), company, kind, cleanKey, string.Empty, NewCode(), mode, now, null, null, "new");
    }

    /// <summary>Sets a feed up — proven by lying on the company's proven domain.</summary>
    public static JobSource Feed(TenantId company, string? address, string domain, PublishMode mode, DateTimeOffset now)
    {
        var provenDomain = (domain ?? string.Empty).Trim().ToLowerInvariant();

        if (provenDomain.Length == 0)
        {
            // web/src/features/company/api/sources.ts matches this sentence.
            throw new SourceError("The company has no proven domain");
        }

        var cleanKey = CleanKey(address);

        if (!OnDomain(cleanKey, provenDomain))
        {
            // web/src/features/company/api/sources.ts matches this sentence.
            throw new SourceError("The feed must lie on the company's own domain");
        }

        return new JobSource(
            Guid.CreateVersion7(), company, SourceKind.Feed, cleanKey, provenDomain, string.Empty, mode, now, now, null, "new");
    }

    /// <summary>The source as a row holds it.</summary>
    public static JobSource Restore(
        Guid id, TenantId company, SourceKind kind, string key, string domain, string code,
        PublishMode mode, DateTimeOffset createdAt, DateTimeOffset? provenAt, DateTimeOffset? fetchedAt, string outcome) =>
        new(id, company, kind, key, domain, code, mode, createdAt, provenAt, fetchedAt, outcome);

    /// <summary>Switches between draft and direct.</summary>
    public void SetMode(PublishMode mode) => Mode = mode;

    /// <summary>Records how a fetch ended.</summary>
    public void Fetched(DateTimeOffset now, string outcome)
    {
        FetchedAt = now;
        Outcome = outcome;
    }

    /// <summary>Proves the board when one of its adverts carries the code; says whether it is proven now.</summary>
    /// <remarks>
    /// Title and text are searched, case aside — whatever field the company
    /// put it in. Once proven it stays proven: the code may leave the advert
    /// again, and a board does not change owners.
    /// </remarks>
    public bool ProveBy(IEnumerable<ExternalAdvert> adverts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(adverts);

        if (IsProven)
        {
            return true;
        }

        if (Code.Length > 0
            && adverts.Any(advert => advert.Title.Contains(Code, StringComparison.OrdinalIgnoreCase)
                                     || advert.Description.Contains(Code, StringComparison.OrdinalIgnoreCase)))
        {
            ProvenAt = now;
        }

        return IsProven;
    }

    /// <summary>Whether an address is https and lies on the domain or below it.</summary>
    public static bool OnDomain(string address, string domain) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private static string CleanKey(string? key)
    {
        var clean = (key ?? string.Empty).Trim();

        if (clean.Length is 0 or > MaxKeyLength)
        {
            // web/src/features/company/api/sources.ts matches this sentence.
            throw new SourceError("The source key is missing or too long");
        }

        return clean;
    }

    /// <summary>Ten letters out of thirty-one, from a cryptographic source — about fifty bits, unguessable.</summary>
    /// <remarks>No 0/o, 1/l/i: somebody types it into their ATS by hand.</remarks>
    private static string NewCode() =>
        CodePrefix + System.Security.Cryptography.RandomNumberGenerator.GetString(CodeLetters, 10);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$")]
    private static partial Regex BoardKey();
}

/// <summary>Where sources live.</summary>
public interface IJobSourceStore
{
    /// <summary>One source, or <c>null</c>.</summary>
    Task<JobSource?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This company's sources.</summary>
    Task<IReadOnlyList<JobSource>> ForCompanyAsync(TenantId company, CancellationToken cancellationToken = default);

    /// <summary>The proven sources not fetched since <paramref name="before"/>.</summary>
    Task<IReadOnlyList<JobSource>> DueAsync(DateTimeOffset before, CancellationToken cancellationToken = default);

    /// <summary>Every proven domain a feed stands on — what the egress boundary learns.</summary>
    Task<IReadOnlyList<string>> DomainsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(JobSource source, CancellationToken cancellationToken = default);

    /// <summary>Removes it.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
