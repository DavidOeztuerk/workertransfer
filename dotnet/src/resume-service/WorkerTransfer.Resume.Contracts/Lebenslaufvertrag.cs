using System.Text.Json.Serialization;

namespace WorkerTransfer.Resume.Contracts;

/// <summary>One position on the wire.</summary>
/// <remarks>
/// The months are strings in the form <c>YYYY-MM</c>, not dates. A date type
/// would force a day onto every entry, and the domain refuses to hold one —
/// no résumé names the 14th of March.
/// <para>
/// The property names are snake_case and stay that way. The browser app was
/// written against the Python service, and a boundary that renames its fields
/// in a migration breaks every caller for no gain (see identity-service, which
/// does the same).
/// </para>
/// </remarks>
public sealed record StationV1(
    [property: JsonPropertyName("employer")] string Employer,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string Description = "");

/// <summary>One stretch of education on the wire.</summary>
public sealed record AusbildungV1(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("qualification")] string Qualification,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn);

/// <summary>A résumé as it is read.</summary>
/// <remarks>
/// Carries no visibility field of any kind, and must not grow one: whether it
/// may be shown lives only in the consent ledger (ADR-0020). A flag here would
/// be a second truth, and the two would disagree the first time somebody
/// withdrew.
/// </remarks>
public sealed record LebenslaufV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("positions")] IReadOnlyList<StationV1> Positions,
    [property: JsonPropertyName("education")] IReadOnlyList<AusbildungV1> Education,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>A résumé as it is written.</summary>
/// <remarks>
/// No subject id. Whose résumé this is comes from the verified token, so a
/// caller cannot write into somebody else's.
/// </remarks>
public sealed record LebenslaufSpeichernV1(
    [property: JsonPropertyName("positions")] IReadOnlyList<StationV1> Positions,
    [property: JsonPropertyName("education")] IReadOnlyList<AusbildungV1> Education);

/// <summary>A request as either side sees it.</summary>
/// <param name="Active">
/// Whether the release holds right now — <c>null</c> where the reader is not
/// entitled to that answer.
/// </param>
/// <remarks>
/// The person sees it filled, because it is the one place where "was granted"
/// and "holds now" visibly come apart. The company sees <c>null</c>: it already
/// has the answer in the form of the data it does or does not get, and a field
/// here could be polled without ever reading a résumé.
/// <para>
/// There is no score, no rank and no percentage on this record, and none
/// belongs on it (ADR-0022).
/// </para>
/// </remarks>
public sealed record AnfrageV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("answered_at")] DateTimeOffset? AnsweredAt,
    [property: JsonPropertyName("active")] bool? Active);
