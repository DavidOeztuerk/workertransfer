using System.Text.Json.Serialization;

namespace WorkerTransfer.Assessment.Contracts;

/// <summary>What a company sends to set a task.</summary>
/// <remarks>
/// <para><c>hours</c> and <c>due_at</c> are <strong>required</strong>, and that
/// is the whole point of this service (ADR-0042 §3). An optional size would be a
/// size one leaves out — and a work sample without a stated size is a task
/// whose price the person learns only after paying it.</para>
///
/// <para>That is also why there is no default here: <c>hours = 4</c> would be a
/// number the server makes up and the person takes for the company's
/// statement.</para>
/// </remarks>
public sealed record SetTaskV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("task")] string? Task,
    [property: JsonPropertyName("hours")] int? Hours,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt);

/// <summary>What the person sends to submit.</summary>
/// <remarks>
/// Text, an address, or both — but not nothing. And no file: this service
/// accepts no bytes, because a third document store would be a third place the
/// erasure has to reach.
/// </remarks>
public sealed record SubmitV1(
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("url")] string? Url = null);

/// <summary>What a company sends to evaluate.</summary>
/// <remarks>
/// <strong>Both fields are required, with <c>rejected</c> too.</strong>
/// Rejecting and giving a reason are one step and not two — an empty text is
/// 422 (ADR-0042 §2).
/// </remarks>
public sealed record EvaluateV1(
    [property: JsonPropertyName("outcome")] string? Outcome = null,
    [property: JsonPropertyName("text")] string? Text = null);

/// <summary>The submitted solution.</summary>
public sealed record SubmissionV1(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset SubmittedAt);

/// <summary>The feedback — text and an outcome, never a number.</summary>
/// <remarks>
/// <para>No <c>score</c>, no <c>rating</c>, no stars, no "3 out of 5". A number
/// hides exactly what helps — what the result lacked — and looks like a
/// measurement while doing it (ADR-0022, ADR-0042 §1).</para>
///
/// <para><c>outcome</c> is <c>accepted</c> or <c>rejected</c> and is about the
/// <em>case</em>: "we go on" or "we do not go on". There is no place where such
/// outcomes are counted across cases.</para>
/// </remarks>
public sealed record EvaluationV1(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("evaluated_at")] DateTimeOffset EvaluatedAt);

/// <summary>
/// A case — and the only type this service hands out about it.
/// </summary>
/// <remarks>
/// <para><strong>Both sides get this document, byte for byte the same.</strong>
/// There is no company view beside a person view, and that is the mechanism
/// behind "the person sees the evaluation, always" (ADR-0042 §2): two views
/// would be the place where the two drift apart — and only months later, at the
/// next field. A test compares the two responses byte for byte.</para>
///
/// <para><c>submission</c> and <c>evaluation</c> are absent as long as they do
/// not exist, instead of being <c>null</c>. Here expressly <em>not</em> to hide
/// anything — both sides see the same — but because a field that never has a
/// value is not a field.</para>
/// </remarks>
public sealed record CaseV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("hours")] int Hours,
    [property: JsonPropertyName("due_at")] DateTimeOffset DueAt,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    /// <summary>The solution, once it is there.</summary>
    [JsonPropertyName("submission")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubmissionV1? Submission { get; init; }

    /// <summary>The feedback, once it is there — for both sides.</summary>
    [JsonPropertyName("evaluation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvaluationV1? Evaluation { get; init; }
}

/// <summary>A list of cases.</summary>
/// <remarks>
/// No total (ADR-0026): it would count across cases, and that is exactly the
/// step by which feedback on a case turns into a statement about a human
/// being.
/// </remarks>
public sealed record CaseListV1(
    [property: JsonPropertyName("items")] IReadOnlyList<CaseV1> Items);

/// <summary>What goes to notification-service: about whom, and of which kind.</summary>
/// <remarks>
/// <para>A <em>typed</em> contract and not an anonymous object.
/// <c>new { userId = … }</c> compiles, serialises, and arrives as
/// <c>Guid.Empty</c> at a receiver that declares
/// <c>[JsonPropertyName("user_id")]</c> — measured at four hops that never
/// arrived because of it.</para>
///
/// <para>Two fields, and there must never be more. No company name, no task
/// text, no evaluation: a mail lands in an inbox, and that inbox may be the
/// current employer's.</para>
/// </remarks>
public sealed record WorkSampleNotificationV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);
