using System.Text.Json.Serialization;

namespace WorkerTransfer.Advisor.Contracts;

/// <summary>The mandate as the person sees and writes it.</summary>
/// <remarks>
/// <para>Four values and a timestamp — and expressly <strong>no</strong>
/// visibility field. Visibility lives in the ledger (ADR-0020); a switch in
/// this place would be a second truth, and one the client could send
/// along.</para>
///
/// <para>Every field is nullable, because "nothing said" is the normal case. An
/// empty mandate is complete.</para>
/// </remarks>
public sealed record MandateV1(
    [property: JsonPropertyName("entry_month")] string? EntryMonth,
    [property: JsonPropertyName("salary_min")] int? SalaryMin,
    [property: JsonPropertyName("salary_max")] int? SalaryMax,
    [property: JsonPropertyName("workload_percent")] int? WorkloadPercent,
    [property: JsonPropertyName("excluded_domains")] IReadOnlyList<string> ExcludedDomains,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>What the person sends to write their mandate.</summary>
public sealed record WriteMandateV1(
    [property: JsonPropertyName("entry_month")] string? EntryMonth = null,
    [property: JsonPropertyName("salary_min")] int? SalaryMin = null,
    [property: JsonPropertyName("salary_max")] int? SalaryMax = null,
    [property: JsonPropertyName("workload_percent")] int? WorkloadPercent = null,
    [property: JsonPropertyName("excluded_domains")] IReadOnlyList<string>? ExcludedDomains = null);

/// <summary>What a company sends to open a conversation.</summary>
public sealed record OpenConversationV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("note")] string? Note = null);

/// <summary>Which stage is meant.</summary>
public sealed record StageV1([property: JsonPropertyName("stage")] int? Stage = null);

/// <summary>A conversation as the PERSON sees it.</summary>
/// <remarks>
/// They see everything: which company, where it stands, how far they have
/// opened it. They learn nothing new about themselves here — it is their own
/// mandate.
/// </remarks>
public sealed record MyConversationV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("stage")] int Stage,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("opened_at")] DateTimeOffset OpenedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>
/// A conversation as the COMPANY sees it — and the most important type of this
/// service.
/// </summary>
/// <remarks>
/// <para><strong>What a stage has not released does not exist here.</strong>
/// Not as <c>null</c>, not as "locked", not as an empty string: the field is
/// <em>absent</em> from the JSON. That is why every stage-dependent field of
/// this class carries <see cref="JsonIgnoreAttribute"/> with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/>.</para>
///
/// <para>The difference is not cosmetic. A <c>"salary_min": null</c> says:
/// there is a field for a salary, you just do not see it — and with that, that
/// there would be something to see. An absent field says nothing. And it says
/// the <em>same</em> nothing when the person has released the stage but never
/// named a salary: hidden and not present are indistinguishable (ADR-0020 §1,
/// ADR-0037).</para>
///
/// <para><c>stage</c> itself is always there, and that is no contradiction: how
/// far a conversation is open is a statement about the <em>conversation</em>
/// and not about hidden contents. Without it a company would not know whether
/// to keep asking — and a service that keeps quiet about that creates exactly
/// the follow-up questions it wants to prevent.</para>
/// </remarks>
public sealed record ConversationV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("stage")] int Stage,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("opened_at")] DateTimeOffset OpenedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    /// <summary>From stage 1: the entry month named.</summary>
    [JsonPropertyName("entry_month")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntryMonth { get; init; }

    /// <summary>From stage 1: the workload named.</summary>
    [JsonPropertyName("workload_percent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WorkloadPercent { get; init; }

    /// <summary>From stage 2: the lower bound of the salary range.</summary>
    [JsonPropertyName("salary_min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SalaryMin { get; init; }

    /// <summary>From stage 2: the upper one.</summary>
    [JsonPropertyName("salary_max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SalaryMax { get; init; }

    /// <summary>From stage 3: the civil name.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>From stage 3: the address one can answer at.</summary>
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }
}

/// <summary>A list of conversations.</summary>
/// <remarks>
/// No total (ADR-0026): through the difference to the length it would reveal
/// how many conversations are currently <em>not</em> at stage 1 — and with
/// that, how many human beings have withdrawn.
/// </remarks>
public sealed record ConversationListV1(
    [property: JsonPropertyName("items")] IReadOnlyList<ConversationV1> Items);

/// <summary>The same for the person.</summary>
public sealed record MyConversationListV1(
    [property: JsonPropertyName("items")] IReadOnlyList<MyConversationV1> Items);

/// <summary>What goes to notification-service: about whom, and of which kind.</summary>
/// <remarks>
/// <para>A <em>typed</em> contract and not an anonymous object.
/// <c>new { userId = … }</c> compiles, serialises, and arrives as
/// <c>Guid.Empty</c> at a receiver that declares
/// <c>[JsonPropertyName("user_id")]</c> — measured on four hops that never
/// arrived because of it.</para>
///
/// <para>Two fields, and there must never be more. No company name, no text: a
/// mail lands in an inbox, and that inbox may be the current employer's.</para>
/// </remarks>
public sealed record ConversationNotificationV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);
