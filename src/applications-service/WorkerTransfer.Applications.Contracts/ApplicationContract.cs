using System.Text.Json.Serialization;

namespace WorkerTransfer.Applications.Contracts;

/// <summary>An application, as it is read.</summary>
/// <remarks>
/// Carries <em>no</em> profile data — only a <c>subject_id</c>. Whoever wants to
/// see profile, résumé or portfolio asks the responsible services, and there the
/// consent ledger applies. A second way to the same data would have a second
/// filter, and that one drifts from the first one day.
/// <para>
/// And no score, no rank, no ordering. A list of applicants sorted by something
/// this service computed is candidate rating through the back door (ADR-0022).
/// </para>
/// </remarks>
public sealed record ApplicationV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("shares_resume")] bool SharesResume,
    [property: JsonPropertyName("shares_portfolio")] bool SharesPortfolio,
    /// <summary>Which documents were enclosed — the ids, in their order.</summary>
    /// <remarks>
    /// A snapshot: what actually went out, not "everything the person has". The
    /// content comes via resume-service and is checked against the ledger there
    /// — only the ids stand here (ADR-0035).
    /// </remarks>
    [property: JsonPropertyName("documents")] IReadOnlyList<Guid> Documents,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>
    /// The letterhead at the moment of sending (ADR-0038). A snapshot, not the
    /// current account address. Never in the <c>CoverLetterContext</c>.
    /// </summary>
    [property: JsonPropertyName("applicant_contact")] ApplicantContactV1 ApplicantContact);

/// <summary>The letterhead, as it was recorded when sending.</summary>
public sealed record ApplicantContactV1(
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("line1")] string Line1,
    [property: JsonPropertyName("line2")] string Line2,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("email")] string Email);

/// <summary>What a person sends to apply.</summary>
/// <remarks>
/// Without <c>subject_id</c> and without <c>tenant_id</c>: who applies stands in
/// the verified token, and whom the job belongs to is said by jobs-service. Both
/// in the body would be a way to apply in someone else's name.
/// <para>
/// <c>shares_resume</c> and <c>shares_portfolio</c> can be chosen, "share the
/// profile" cannot — an application without any statement about the person is
/// none.
/// </para>
/// </remarks>
public sealed record SubmitApplicationV1(
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("message")] string Message = "",
    [property: JsonPropertyName("shares_resume")] bool SharesResume = false,
    [property: JsonPropertyName("shares_portfolio")] bool SharesPortfolio = false);

/// <summary>Where the company moves the application.</summary>
public sealed record MoveApplicationV1(
    [property: JsonPropertyName("status")] string Status);

/// <summary>Numbers about one's <em>own</em> cases.</summary>
/// <remarks>
/// Nothing here offsets applications against market status, résumé or cases at
/// other companies. The line does not run at the aggregation but at the merging
/// (ADR-0022/0026).
/// </remarks>
public sealed record ApplicationCountsV1(
    [property: JsonPropertyName("by_status")] IReadOnlyDictionary<string, int> ByStatus,
    [property: JsonPropertyName("total")] int Total);

/// <summary>A comment on the draft.</summary>
/// <remarks>
/// <c>quote</c> is the marked passage. It travels along so the next reader knows
/// what the comment referred to — even when the text has changed since.
/// </remarks>
public sealed record CommentV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("resolved")] bool Resolved,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>A cover letter on its way to becoming an application.</summary>
/// <remarks>
/// No score, no mark, no ordering: what stands here is the person's text and
/// the state of its way (ADR-0034).
/// </remarks>
public sealed record DraftV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("shares_resume")] bool SharesResume,
    [property: JsonPropertyName("documents")] IReadOnlyList<Guid> Documents,
    [property: JsonPropertyName("comments")] IReadOnlyList<CommentV1> Comments,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("writing_started_at")] DateTimeOffset? WritingStartedAt = null);

/// <summary>For which jobs drafts are to come about.</summary>
public sealed record CreateDraftsV1(
    [property: JsonPropertyName("job_ids")] IReadOnlyList<Guid> JobIds);

/// <summary>One's own changes to the text.</summary>
public sealed record ChangeDraftV1(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("body")] string? Body);

/// <summary>What goes along.</summary>
public sealed record AttachmentsV1(
    [property: JsonPropertyName("shares_resume")] bool SharesResume,
    [property: JsonPropertyName("documents")] IReadOnlyList<Guid>? Documents);

/// <summary>Write a comment.</summary>
public sealed record AddCommentV1(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("quote")] string? Quote);
