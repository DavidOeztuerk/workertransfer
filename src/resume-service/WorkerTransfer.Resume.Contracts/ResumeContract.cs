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
public sealed record PositionV1(
    [property: JsonPropertyName("employer")] string Employer,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string Description = "",
    /// <summary>What was worked with there — named by the person themselves.</summary>
    /// <remarks>
    /// Canonicalised by the same vocabulary as the profile, so that "postgres"
    /// is called "PostgreSQL" here as there (ADR-0023). They do not make the
    /// position searchable: a résumé is released one company at a time
    /// (ADR-0020), and a skill becomes searchable only on the profile.
    /// </remarks>
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>One stretch of education on the wire.</summary>
/// <param name="Kind">
/// <c>schule</c> or <c>ausbildung</c> — wire words, shared with the web app.
/// Missing means vocational training: older rows do not carry the field.
/// </param>
public sealed record EducationV1(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("qualification")] string Qualification,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("kind")] string? Kind = null);

/// <summary>A résumé as it is read.</summary>
/// <remarks>
/// Carries no visibility field of any kind, and must not grow one: whether it
/// may be shown lives only in the consent ledger (ADR-0020). A flag here would
/// be a second truth, and the two would disagree the first time somebody
/// withdrew.
/// </remarks>
public sealed record ResumeV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("positions")] IReadOnlyList<PositionV1> Positions,
    [property: JsonPropertyName("education")] IReadOnlyList<EducationV1> Education,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>The template the résumé is typeset in.</summary>
    /// <remarks>
    /// A NAME, not a layout: what it looks like lies in the interface and holds
    /// for both sides — the person sees the same as the company they send the
    /// résumé to (ADR-0035). The value is one of the wire words
    /// <c>schlicht</c>, <c>klassisch</c>, <c>modern</c>.
    /// </remarks>
    [property: JsonPropertyName("template")] string Template = "schlicht");

/// <summary>An enclosed document — without its bytes.</summary>
/// <remarks>
/// The content comes over an address of its own, so that a list does not drag
/// a dozen files along. <c>content_type</c> is what the SIGNATURE showed, never
/// what the uploader claimed.
/// </remarks>
public sealed record DocumentRecordV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("size_bytes")] int SizeBytes,
    [property: JsonPropertyName("uploaded_at")] DateTimeOffset UploadedAt);

/// <summary>What was read in a document — or that it was never read.</summary>
/// <param name="ReadAt">
/// <c>null</c> means <strong>never read</strong>. That state is not the same as
/// "read and nothing found", and the two must never collapse: otherwise the
/// interface would tell somebody silently that their master craftsman's
/// certificate says nothing (ADR-0022 §3).
/// </param>
/// <param name="HasText">
/// Whether there was any text to read at all. A photographed journeyman's
/// certificate is a picture — it holds nothing that could be found without OCR
/// on images, and the answer says so instead of staying silent.
/// </param>
/// <param name="Terms">
/// The canonical names the vocabulary recognised in the text. <strong>Evidence</strong>
/// and not a naming: a statement about this document, searchable nowhere, and
/// only a click and a save on the profile turn it into a statement about the
/// human being (ADR-0033).
/// <para>
/// <strong>No wording.</strong> The text found goes neither out nor into the
/// database; only the names go out.
/// </para>
/// </param>
public sealed record DocumentTermsV1(
    [property: JsonPropertyName("document_id")] Guid DocumentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("read_at")] DateTimeOffset? ReadAt,
    [property: JsonPropertyName("has_text")] bool HasText,
    [property: JsonPropertyName("terms")] IReadOnlyList<string> Terms);

/// <summary>Which template is chosen.</summary>
public sealed record ChooseTemplateV1(
    [property: JsonPropertyName("template")] string Template);

/// <summary>A résumé as it is written.</summary>
/// <remarks>
/// No subject id. Whose résumé this is comes from the verified token, so a
/// caller cannot write into somebody else's.
/// </remarks>
public sealed record StoreResumeV1(
    [property: JsonPropertyName("positions")] IReadOnlyList<PositionV1> Positions,
    [property: JsonPropertyName("education")] IReadOnlyList<EducationV1> Education);

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
public sealed record RequestV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("answered_at")] DateTimeOffset? AnsweredAt,
    [property: JsonPropertyName("active")] bool? Active);
