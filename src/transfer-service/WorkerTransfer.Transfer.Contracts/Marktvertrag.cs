using System.Text.Json.Serialization;

namespace WorkerTransfer.Transfer.Contracts;

/// <summary>Ein MarketStatus, wie er gelesen wird.</summary>
public sealed record MarketStatusV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("employed")] bool Employed,
    [property: JsonPropertyName("note")] string Note,
    /// <summary>Abgeleitet mitgeschickt.</summary>
    /// <remarks>
    /// Sonst reimt sich jeder Client die Regel selbst zusammen, und irgendeiner
    /// reimt sie falsch: „freigegeben" heißt nicht „ansprechbar".
    /// </remarks>
    [property: JsonPropertyName("is_approachable")] bool IsApproachable,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Ein MarketStatus, wie er geschrieben wird.</summary>
/// <remarks>
/// Ohne <c>subject_id</c>: wessen Status es ist, steht im geprüften Token. Ein
/// Feld dafür wäre ein Weg, den MarketStatus eines anderen zu setzen — und das
/// ist die gefährlichste Angabe im ganzen System.
/// </remarks>
public sealed record WriteMarketStatusV1(
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("employed")] bool Employed = false,
    [property: JsonPropertyName("note")] string Note = "");

/// <summary>Die ResumeRequest eines Unternehmens nach einem MarketStatus.</summary>
public sealed record MarketRequestV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("answered_at")] DateTimeOffset? AnsweredAt,
    /// <summary>Ob der Zugriff <em>gerade</em> gilt.</summary>
    /// <remarks>
    /// <c>null</c> für das anfragende Unternehmen: es hat die Antwort schon in
    /// Form des Status, den es sieht oder nicht sieht — und ein Feld hier
    /// ließe sich abfragen, ohne je einen MarketStatus zu lesen.
    /// </remarks>
    [property: JsonPropertyName("active")] bool? Active);

/// <summary>Ein Transfer-Case, wie er gelesen wird.</summary>
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

/// <summary>Was ein Unternehmen schickt, um Interesse zu zeigen.</summary>
public sealed record InteresseZeigenV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message = "");

/// <summary>Ein Angebot.</summary>
public sealed record AngebotV1(
    [property: JsonPropertyName("note")] string Note = "",
    [property: JsonPropertyName("start_on")] string? StartOn = null,
    [property: JsonPropertyName("fee_cents")] long? FeeCents = null);
