using System.Text.Json.Serialization;

namespace WorkerTransfer.Jobs.Contracts;

/// <summary>An advert, as it is read.</summary>
/// <remarks>
/// Carries no score, no rank and no percentage — and none belongs on it. How
/// well somebody suits a job is computed in the browser, shown to the
/// <em>person</em> and never to the company (ADR-0022).
/// <para>
/// <c>skills</c> is the list the browser holds the profile against. That is why
/// it stands here spelled out and not as a number: a number would hide which
/// requirement is missing, and that is exactly the only helpful thing.
/// </para>
/// </remarks>
public sealed record JobV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("remote_mode")] string RemoteMode,
    [property: JsonPropertyName("employment_type")] string EmploymentType,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>
    /// What the job pays — <c>null</c> means "not stated", and the UI says so
    /// (VISION-FRAGEN B5). A number about the job, never about a person.
    /// </summary>
    [property: JsonPropertyName("salary")] SalaryV1? Salary = null);

/// <summary>A salary range: gross, whole euros, both ends.</summary>
public sealed record SalaryV1(
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max,
    /// <summary><c>year</c>, <c>month</c> or <c>hour</c>.</summary>
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("currency")] string Currency = "EUR");

/// <summary>The salary range as the form sends it — both ends or neither.</summary>
public sealed record WriteSalaryV1(
    [property: JsonPropertyName("min")] int? Min,
    [property: JsonPropertyName("max")] int? Max,
    [property: JsonPropertyName("period")] string? Period = "year");

/// <summary>An advert, as it is written.</summary>
/// <remarks>
/// Without <c>tenant_id</c>: whom the advert belongs to stands in the verified
/// token. And without <c>status</c> — a new advert begins as a draft, and the
/// transition is an act of its own with a route of its own.
/// </remarks>
public sealed record WriteJobV1(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("location")] string Location = "",
    [property: JsonPropertyName("postal_code")] string PostalCode = "",
    [property: JsonPropertyName("remote_mode")] string RemoteMode = "none",
    [property: JsonPropertyName("employment_type")] string EmploymentType = "full_time",
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills = null,
    [property: JsonPropertyName("salary")] WriteSalaryV1? Salary = null);

// A PAGE OF ADVERTS NO LONGER EXISTS HERE — it is now `PageResponse<JobV1>`
// from `ServiceDefaults`, so that every pageable list has the same envelope.
//
// The sentence that stood here deserves not to get lost: "without a total, and
// for the same reason as with the candidates: through the difference it would
// say something about what was filtered."
//
// That is true — for CANDIDATES. There the difference between "42 in total" and
// "3 shown" is the information how many people have hidden themselves, and
// exactly that nobody may get (ADR-0026). A job advert, however, is no person:
// it was published by a company in order to be seen, and "9 hits" says nothing
// about anybody.
//
// The candidate list therefore stays with the cursor and gets NO total.

/// <summary>What a company sends to get help with the phrasing.</summary>
public sealed record DraftV1(
    [property: JsonPropertyName("title")] string Title = "",
    [property: JsonPropertyName("description")] string Description = "",
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills = null,
    [property: JsonPropertyName("location")] string Location = "",
    [property: JsonPropertyName("wish")] string Wish = "");
