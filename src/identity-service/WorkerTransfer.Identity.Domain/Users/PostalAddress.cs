using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>
/// A person's postal address — the template for a letterhead and an employer folder.
/// </summary>
/// <remarks>
/// <strong>Purpose limitation (ADR-0038).</strong> Street and telephone belong
/// on the letter, not in the search, not in the token, not at a model. Whoever
/// never stores an address has no row — privacy by default, Art. 25.
/// <para>
/// A subject-keyed table: the key IS the person. The erasure guard finds the
/// table through the annotation, not through a <c>subject_id</c> column beside
/// it.
/// </para>
/// </remarks>
public sealed class PostalAddress
{
    private PostalAddress(SubjectId subject)
    {
        Subject = subject;
    }

    /// <summary>Whose. And at the same time the key.</summary>
    public SubjectId Subject { get; }

    /// <summary>Street and house number.</summary>
    public string Line1 { get; private set; } = string.Empty;

    /// <summary>The second address line, or empty.</summary>
    public string Line2 { get; private set; } = string.Empty;

    /// <summary>Postal code.</summary>
    public string PostalCode { get; private set; } = string.Empty;

    /// <summary>Town.</summary>
    public string City { get; private set; } = string.Empty;

    /// <summary>ISO-3166-1 alpha-2, default DE.</summary>
    public string Country { get; private set; } = "DE";

    /// <summary>Telephone, optional.</summary>
    public string Phone { get; private set; } = string.Empty;

    /// <summary>Nothing stored yet.</summary>
    public static PostalAddress Empty(SubjectId subject) => new(subject);

    /// <summary>As the row holds it.</summary>
    public static PostalAddress Restore(
        SubjectId subject,
        string line1,
        string line2,
        string postalCode,
        string city,
        string country,
        string phone)
    {
        var address = new PostalAddress(subject);
        address.Set(line1, line2, postalCode, city, country, phone);
        return address;
    }

    /// <summary>Writes the template. Empty fields stay empty — nothing is compulsory.</summary>
    public void Set(
        string? line1,
        string? line2,
        string? postalCode,
        string? city,
        string? country,
        string? phone)
    {
        Line1 = Truncate(line1, 120);
        Line2 = Truncate(line2, 120);
        PostalCode = Truncate(postalCode, 16);
        City = Truncate(city, 80);
        var iso = Truncate(country, 2).ToUpperInvariant();
        Country = iso.Length == 2 ? iso : "DE";
        Phone = Truncate(phone, 40);
    }

    private static string Truncate(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
