using System.Text.Json.Serialization;

namespace WorkerTransfer.Profile.Contracts;

/// <summary>A profile, as the internal search reports it.</summary>
/// <remarks>
/// <para>Only what the person wrote themselves. No name, no address, no
/// employer — the caller is a service that searches, and what it does not get
/// it cannot pass on.</para>
///
/// <para><c>skills</c> is the provenance class <em>named</em> from ADR-0033 and
/// the only thing searched over. Evidence does not stand here: scout-service
/// fetches it to the hit, from the service that holds it.</para>
///
/// <para><strong>And no visibility field.</strong> Whether this profile may be
/// shown stands in the consent ledger and nowhere else (ADR-0020). The caller
/// asks it itself, for the whole page at once — a field here would be a second
/// truth, and the two would disagree at the first revocation.</para>
/// </remarks>
public sealed record ProfileHitV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    /// <summary>What the person wrote about themselves.</summary>
    /// <remarks>
    /// It stands here because the candidate card showed it before scout-service
    /// replaced it. A move in which a field silently disappears is no move but
    /// a deterioration under another name.
    /// </remarks>
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    /// <summary>The commute stage, or <c>null</c> for "said nothing".</summary>
    /// <remarks>
    /// <strong>The stage travels, not a kilometre figure</strong> (ADR-0041).
    /// What the recipient makes of it is a tick — and <c>null</c> is the third
    /// state there, not a no.
    /// </remarks>
    [property: JsonPropertyName("commute_km")] string? CommuteKm = null,
    [property: JsonPropertyName("relocation")] string? Relocation = null);

/// <summary>A page of the internal search.</summary>
/// <remarks>
/// <strong>Without a total</strong> (ADR-0026) — here too, where only one
/// service reads along: what does not exist cannot be passed on by accident
/// either. The order is <c>updated_at DESC, id DESC</c>: stable and unrelated to
/// the matter, so that two identical searches return the same.
/// </remarks>
public sealed record ProfileHitPageV1(
    [property: JsonPropertyName("items")] IReadOnlyList<ProfileHitV1> Items,
    [property: JsonPropertyName("next")] string? Next);
