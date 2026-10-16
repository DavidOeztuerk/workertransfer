using System.Text.Json.Serialization;

namespace WorkerTransfer.Transfer.Contracts;

/// <summary>A market status as it is read.</summary>
public sealed record MarketStatusV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("employed")] bool Employed,
    [property: JsonPropertyName("note")] string Note,
    /// <summary>Derived and sent along.</summary>
    /// <remarks>
    /// Otherwise every client works the rule out for itself, and one of them
    /// works it out wrongly: "released" does not mean "approachable".
    /// </remarks>
    [property: JsonPropertyName("is_approachable")] bool IsApproachable,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>The person's own window — only in their OWN view, never a company's (H1).</summary>
    [property: JsonPropertyName("open_from")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? OpenFrom = null,
    [property: JsonPropertyName("open_until")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? OpenUntil = null);

/// <summary>A market status as it is written.</summary>
/// <remarks>
/// Without <c>subject_id</c>: whose status it is stands in the verified token.
/// A field for it would be a way to set somebody else's market status — and
/// that is the most dangerous statement in the whole system.
/// </remarks>
public sealed record WriteMarketStatusV1(
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("employed")] bool Employed = false,
    [property: JsonPropertyName("note")] string Note = "",
    [property: JsonPropertyName("open_from")] DateOnly? OpenFrom = null,
    [property: JsonPropertyName("open_until")] DateOnly? OpenUntil = null);

/// <summary>A company's request for a market status.</summary>
public sealed record MarketRequestV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("answered_at")] DateTimeOffset? AnsweredAt,
    /// <summary>Whether the access holds <em>right now</em>.</summary>
    /// <remarks>
    /// <c>null</c> for the asking company: it has the answer already in the form
    /// of the status it sees or does not see — and a field here could be queried
    /// without ever reading a market status.
    /// </remarks>
    [property: JsonPropertyName("active")] bool? Active);

/// <summary>A transfer case as it is read.</summary>
public sealed record TransferV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("requires_release")] bool RequiresRelease,
    [property: JsonPropertyName("release_confirmed")] bool ReleaseConfirmed,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("offer_note")] string OfferNote,
    [property: JsonPropertyName("offer_start_on")] string? OfferStartOn,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>
    /// Only in the PERSON's view, and only with a stated notice period and a
    /// readable start: whether the offer begins before she could leave — then
    /// a buyout or a termination agreement would be needed (H2). Never a price.
    /// </summary>
    [property: JsonPropertyName("starts_before_earliest")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? StartsBeforeEarliest = null);

/// <summary>What a company sends to show interest.</summary>
public sealed record ShowInterestV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message = "");

/// <summary>An offer — text and start, no fee.</summary>
/// <remarks>
/// The fee had no recipient here: it is agreed between two companies in a
/// release negotiation (ADR-0050). A <c>fee_cents</c> sent anyway is ignored,
/// not refused — an old client loses nothing it ever had.
/// </remarks>
public sealed record OfferV1(
    [property: JsonPropertyName("note")] string Note = "",
    [property: JsonPropertyName("start_on")] string? StartOn = null);

/// <summary>The person's notice period and what follows from it — her own view only.</summary>
/// <remarks>All <c>null</c>: not stated. <c>earliest_start</c> assumes notice given today.</remarks>
public sealed record NoticeV1(
    [property: JsonPropertyName("length")] int? Length,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("end")] string? End,
    [property: JsonPropertyName("last_day")] DateOnly? LastDay,
    [property: JsonPropertyName("earliest_start")] DateOnly? EarliestStart);

/// <summary>Stating the notice period.</summary>
public sealed record WriteNoticeV1(
    [property: JsonPropertyName("length")] int Length,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("end")] string? End);

/// <summary>Whether a company lets other companies ask about its staff (ADR-0050).</summary>
public sealed record EmployerSettingV1(
    [property: JsonPropertyName("accepts_inquiries")] bool AcceptsInquiries);

/// <summary>The person's own route to her employer — all <c>null</c> when none is open.</summary>
/// <remarks>
/// <c>open</c> is the ledger's answer at this moment: a revocation on
/// <c>/consents</c> closes the route while the named employer still stands.
/// </remarks>
public sealed record EmployerRouteV1(
    [property: JsonPropertyName("employer_id")] Guid? EmployerId,
    [property: JsonPropertyName("open")] bool Open,
    [property: JsonPropertyName("opened_at")] DateTimeOffset? OpenedAt);

/// <summary>Naming the employer — and with it, consenting to the route.</summary>
public sealed record OpenEmployerRouteV1(
    [property: JsonPropertyName("employer_id")] Guid EmployerId);

/// <summary>A release negotiation as one party reads it (ADR-0050).</summary>
/// <remarks>
/// <para><c>status</c> <c>possible</c> and no <c>id</c>: nothing is asked yet,
/// but the route is open — the hiring company's view only.</para>
///
/// <para><c>regular_from</c> is the person's own earliest regular start (H2),
/// shown here to both companies under her consent; <c>null</c> means she
/// stated no notice period. <c>person_name</c> goes to her employer only.</para>
/// </remarks>
public sealed record ReleaseNegotiationV1(
    [property: JsonPropertyName("id")] Guid? Id,
    [property: JsonPropertyName("transfer_id")] Guid TransferId,
    [property: JsonPropertyName("hiring_id")] Guid HiringId,
    [property: JsonPropertyName("employer_id")] Guid EmployerId,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("release_month")] string? ReleaseMonth,
    [property: JsonPropertyName("fee_cents")] long? FeeCents,
    [property: JsonPropertyName("terms_by")] string? TermsBy,
    [property: JsonPropertyName("regular_from")] DateOnly? RegularFrom,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("person_name")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PersonName = null);

/// <summary>Terms for an earlier release: the month, and a fee or none.</summary>
public sealed record ReleaseTermsV1(
    [property: JsonPropertyName("release_month")] string? ReleaseMonth = null,
    [property: JsonPropertyName("fee_cents")] long? FeeCents = null);
