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
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>A market status as it is written.</summary>
/// <remarks>
/// Without <c>subject_id</c>: whose status it is stands in the verified token.
/// A field for it would be a way to set somebody else's market status — and
/// that is the most dangerous statement in the whole system.
/// </remarks>
public sealed record WriteMarketStatusV1(
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("employed")] bool Employed = false,
    [property: JsonPropertyName("note")] string Note = "");

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
    [property: JsonPropertyName("offer_fee_cents")] long? OfferFeeCents,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>What a company sends to show interest.</summary>
public sealed record ShowInterestV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message = "");

/// <summary>An offer.</summary>
public sealed record OfferV1(
    [property: JsonPropertyName("note")] string Note = "",
    [property: JsonPropertyName("start_on")] string? StartOn = null,
    [property: JsonPropertyName("fee_cents")] long? FeeCents = null);
