using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>
/// Which role somebody holds in a company — the answer for
/// <c>GET /internal/companies/{tenantId}/members/{subjectId}/role</c>.
/// </summary>
/// <remarks>
/// <para><strong>A TYPED contract, not an anonymous object.</strong> Four
/// notification wires never arrived because in their place stood a
/// <c>new { … }</c> and the receiver expected a different property. A wire an
/// access decision hangs on must not be able to do that: a field left unread
/// would silently be "not an admin" here.</para>
///
/// <para><strong>One question, one answer.</strong> The alternative would be to
/// widen the existing member list — then the whole staff would travel over the
/// wire for every permission check, only to read one line out of it. Here
/// exactly what was asked goes out.</para>
///
/// <para><c>null</c> means "does not belong to it" and is indistinguishable
/// from "does not exist" — the same reason as everywhere else: the difference
/// would be a disclosure about which companies exist.</para>
/// </remarks>
public sealed record CompanyRoleV1(
    [property: JsonPropertyName("role")] string? Rolle);
