using Noelia.Core.Identity;

namespace WorkerTransfer.Companies.Domain.EmployerProfiles;

/// <summary>The employer profile — how a company shows itself.</summary>
/// <remarks>
/// <c>tenants</c> in identity-service holds name and domain: the
/// <strong>identity</strong>, grown out of a proven domain (ADR-0019). This is
/// about <strong>presentation</strong>. The same split as between
/// identity-service and profile-service for a person: one side knows who
/// somebody is, the other how they show themselves.
/// <para>
/// The consent ledger does not appear here, and that is no oversight: nobody
/// is affected who could consent. A company is making a statement about
/// itself.
/// </para>
/// </remarks>
public sealed class EmployerProfile
{
    /// <summary>How long the display name may be.</summary>
    public const int MaxDisplayNameLength = 160;

    /// <summary>How long the self-description may be.</summary>
    public const int MaxAboutLength = 8000;

    /// <summary>How long a link may be.</summary>
    public const int MaxLinkLength = 2000;

    /// <summary>How long one list entry may be.</summary>
    public const int MaxEntryLength = 120;

    /// <summary>How many entries a list may hold.</summary>
    public const int MaxEntries = 20;

    private EmployerProfile(
        TenantId company,
        string slug,
        CheckedValues values,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Firma = company;
        Slug = slug;
        DisplayName = values.DisplayName;
        About = values.About;
        Website = values.Website;
        Locations = values.Locations;
        Benefits = values.Benefits;
        Line1 = values.Line1;
        PostalCode = values.PostalCode;
        City = values.City;
        Country = values.Country;
        Phone = values.Phone;
        CreatedAt = angelegtAm;
        ChangedAt = geaendertAm;
    }

    /// <summary>Which company. It <em>is</em> the key.</summary>
    public TenantId Firma { get; }

    /// <summary>The address of the careers page.</summary>
    /// <remarks>
    /// Assigned once and immutable afterwards — it is a promise, and a slug
    /// that follows the display name breaks every link anybody shared. That is
    /// why this class offers no way to change it.
    /// </remarks>
    public string Slug { get; }

    /// <summary>The brand.</summary>
    /// <remarks>
    /// <strong>Not</strong> <c>tenants.name</c>: one is the account name given
    /// at registration, the other is the brand. Neither is derived from the
    /// other, so there is no copy here that could drift.
    /// </remarks>
    public string DisplayName { get; private set; }

    /// <summary>What the company writes about itself.</summary>
    public string About { get; private set; }

    /// <summary><c>null</c> when none was given.</summary>
    public string? Website { get; private set; }

    /// <summary>Where the work happens.</summary>
    public IReadOnlyList<string> Locations { get; private set; }

    /// <summary>What is on offer.</summary>
    public IReadOnlyList<string> Benefits { get; private set; }

    /// <summary>Street for the letterhead. Empty when none is stored.</summary>
    public string Line1 { get; private set; }

    public string PostalCode { get; private set; }

    public string City { get; private set; }

    public string Country { get; private set; }

    public string Phone { get; private set; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Creates a profile.</summary>
    /// <exception cref="ProfileError">Something about the input is wrong.</exception>
    public static EmployerProfile Create(
        TenantId company,
        string slug,
        string displayName,
        string about,
        string? website,
        IReadOnlyList<string>? locations,
        IReadOnlyList<string>? benefits,
        DateTimeOffset now,
        string? line1 = null,
        string? postalCode = null,
        string? ort = null,
        string? land = null,
        string? phone = null) =>
        new(company, slug,
            Check(displayName, about, website, locations, benefits,
                line1, postalCode, ort, land, phone),
            now, now);

    /// <summary>The profile as a row holds it.</summary>
    public static EmployerProfile Restore(
        TenantId company,
        string slug,
        string displayName,
        string about,
        string? website,
        IReadOnlyList<string> locations,
        IReadOnlyList<string> benefits,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        string line1 = "",
        string postalCode = "",
        string ort = "",
        string land = "DE",
        string phone = "") =>
        new(company, slug,
            new CheckedValues(displayName, about, website, locations, benefits,
                line1, postalCode, ort, land, phone),
            angelegtAm, geaendertAm);

    /// <summary>Rewrites the fields — not the slug.</summary>
    /// <exception cref="ProfileError">Something about the input is wrong.</exception>
    public void Change(
        string displayName,
        string about,
        string? website,
        IReadOnlyList<string>? locations,
        IReadOnlyList<string>? benefits,
        DateTimeOffset now,
        string? line1 = null,
        string? postalCode = null,
        string? ort = null,
        string? land = null,
        string? phone = null)
    {
        // Check everything first, then write: a rejected form must not leave a
        // half-changed aggregate behind.
        var checkedValues = Check(displayName, about, website, locations, benefits,
            line1, postalCode, ort, land, phone);

        DisplayName = checkedValues.DisplayName;
        About = checkedValues.About;
        Website = checkedValues.Website;
        Locations = checkedValues.Locations;
        Benefits = checkedValues.Benefits;
        Line1 = checkedValues.Line1;
        PostalCode = checkedValues.PostalCode;
        City = checkedValues.City;
        Country = checkedValues.Country;
        Phone = checkedValues.Phone;
        ChangedAt = now;
    }

    /// <summary>The checkedValues values, typed.</summary>
    /// <remarks>
    /// Shared by create and change so the two cannot drift apart — five fields
    /// that are all strings or lists of strings are exactly the mix-up nobody
    /// notices.
    /// </remarks>
    private sealed record CheckedValues(
        string DisplayName,
        string About,
        string? Website,
        IReadOnlyList<string> Locations,
        IReadOnlyList<string> Benefits,
        string Line1,
        string PostalCode,
        string City,
        string Country,
        string Phone);

    private static CheckedValues Check(
        string displayName,
        string about,
        string? website,
        IReadOnlyList<string>? locations,
        IReadOnlyList<string>? benefits,
        string? line1 = null,
        string? postalCode = null,
        string? ort = null,
        string? land = null,
        string? phone = null)
    {
        var iso = Text("country", land, required: false, 2).ToUpperInvariant();
        return new(
            Text("Display name", displayName, required: true, MaxDisplayNameLength),
            Text("About", about, required: false, MaxAboutLength),
            Link(website),
            Entries("locations", locations),
            Entries("benefits", benefits),
            Text("line1", line1, required: false, 120),
            Text("postal_code", postalCode, required: false, 16),
            Text("city", ort, required: false, 80),
            iso.Length == 2 ? iso : "DE",
            Text("phone", phone, required: false, 40));
    }

    private static string Text(string field, string? value, bool required, int limit)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (required && trimmed.Length == 0)
        {
            throw new TextError(field, "must not be empty");
        }

        return trimmed.Length > limit
            ? throw new TextError(field, $"exceeds {limit} characters")
            : trimmed;
    }

    private static string? Link(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            // Empty and "not given" are the same thing; an empty string would
            // be rendered as a link and lead nowhere.
            return null;
        }

        if (trimmed.Length > MaxLinkLength)
        {
            throw new LinkError($"The link exceeds {MaxLinkLength} characters");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var address))
        {
            throw new LinkError("The link is missing a host");
        }

        if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
        {
            throw new LinkError("Only http and https links are allowed");
        }

        return string.IsNullOrEmpty(address.Host)
            ? throw new LinkError("The link is missing a host")
            : trimmed;
    }

    /// <summary>Trimmed, no blanks, no duplicates — the order survives.</summary>
    /// <remarks>
    /// Deduplicate first, then count: otherwise somebody who wrote "remote"
    /// twenty-one times would be refused, although it collapses into one entry.
    /// </remarks>
    private static IReadOnlyList<string> Entries(string field, IReadOnlyList<string>? raw)
    {
        if (raw is null)
        {
            return [];
        }

        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in raw)
        {
            var trimmed = (entry ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.Length > MaxEntryLength)
            {
                throw new TextError(field, $"entry exceeds {MaxEntryLength} characters");
            }

            if (seen.Add(trimmed))
            {
                kept.Add(trimmed);
            }
        }

        return kept.Count > MaxEntries
            ? throw new TooManyEntries(field, MaxEntries)
            : kept;
    }
}

/// <summary>Finds and stores employer profiles.</summary>
public interface IProfileStore
{
    /// <summary>This company's profile, or <c>null</c>.</summary>
    Task<EmployerProfile?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>The profile behind this slug, or <c>null</c>.</summary>
    Task<EmployerProfile?> GetAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The wanted slug, or the next free one with a counter.</summary>
    /// <remarks>
    /// <strong>Do not append the tenant id</strong>: it would then sit in an
    /// address people pass around.
    /// </remarks>
    Task<string> FreeSlugAsync(string wanted, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(EmployerProfile profile, CancellationToken cancellationToken = default);
}
