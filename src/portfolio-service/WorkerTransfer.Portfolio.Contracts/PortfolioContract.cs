using System.Text.Json.Serialization;

namespace WorkerTransfer.Portfolio.Contracts;

/// <summary>An entry on the wire.</summary>
/// <remarks>
/// The field names are snake_case and stay that way. The browser app was
/// written against the Python service, and a boundary that renames its fields
/// during a migration breaks every caller for nothing in return.
/// <para>
/// Carries <b>no visibility</b> and must never get one: whether a portfolio
/// may be shown stands only in the consent ledger (ADR-0020 §6).
/// </para>
/// </remarks>
public sealed record EntryV1(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary = "",
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("role")] string Role = "",
    [property: JsonPropertyName("year")] int? Year = null,
    [property: JsonPropertyName("attachment")] string? Attachment = null,
    /// <summary>What was worked with — named by the person themselves.</summary>
    /// <remarks>
    /// Canonicalised by the same vocabulary as the profile (ADR-0023). They do
    /// not make the work searchable: a skill becomes searchable only in the
    /// profile, and it gets there with a click.
    /// </remarks>
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>A portfolio as it is read.</summary>
public sealed record PortfolioV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("items")] IReadOnlyList<EntryV1> Items,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>A portfolio as it is written.</summary>
/// <remarks>
/// Without <c>subject_id</c>: whose portfolio it is stands in the verified
/// token.
/// </remarks>
public sealed record WritePortfolioV1(
    [property: JsonPropertyName("items")] IReadOnlyList<EntryV1> Items);
