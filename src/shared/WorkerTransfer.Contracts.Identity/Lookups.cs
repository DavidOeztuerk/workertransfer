using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>Who a company is: a name and a proven domain.</summary>
/// <remarks>
/// <para>The answer of <c>GET /internal/companies/{tenantId}</c>, behind the
/// shared secret and without a gateway route.</para>
///
/// <para><strong>The domain is the reason this contract exists.</strong> A
/// person may exclude companies and names them by their domain (ADR-0037). It
/// is not a disclosure about a human being: it stands on every careers page,
/// and it grew out of a <em>confirmed</em> address (ADR-0019), so it cannot be
/// forged.</para>
///
/// <para>What does <strong>not</strong> stand here is the staff. For that there
/// is <see cref="CompanyMembersResponseV1"/>, and that is a door of its own with
/// a question of its own.</para>
/// </remarks>
public sealed record CompanyLookupV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("domain")] string Domain);

/// <summary>What a human being is called, and where to answer them.</summary>
/// <remarks>
/// <para>The answer of <c>GET /internal/account/{subjectId}/identity</c>.</para>
///
/// <para><strong>Two fields, and they must never become more.</strong> No
/// postal address and no phone: ADR-0038 keeps the postal address as a template
/// for a letterhead, and it belongs to an application the person sends
/// themselves — not in the view of a company that is currently holding a
/// conversation.</para>
///
/// <para><strong>The secret at the endpoint is not the whole permission.</strong>
/// It proves that a service is asking, not that it may ask: the permission
/// stands in the ledger (<c>advisor.identity:tenant:&lt;id&gt;</c>) and is
/// fetched <em>before</em> this call. Whoever uses this door for something else
/// fetches the check along with it.</para>
/// </remarks>
public sealed record PersonLookupV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email);
