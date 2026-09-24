using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>A member of a company, as it travels over the internal wire.</summary>
/// <remarks>
/// This is a TYPED CONTRACT. It is handed service to service, never as an
/// anonymous object. The notification wire never arrived four times over,
/// precisely because in its place stood a <c>new { ... }</c> — and the
/// serialiser at the receiver expected a different property. A contract that
/// lies here is checked by COMPILATION on both sides.
/// <para>
/// Only the identifier. A display name would travel along and end up in every
/// backup, and the outbox does not need it: it carries an identifier and a kind
/// (ADR-0025), nothing else.
/// </para>
/// </remarks>
public sealed record CompanyMemberV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId);

/// <summary>The answer for GET /internal/companies/{tenantId}/members.</summary>
/// <remarks>
/// The wire name <c>mitglieder</c> is German and stays: it is a contract between
/// identity-service and applications-service, and <c>MemberWireTests</c> pins it.
/// Renaming it is a contract change, not a refactor.
/// </remarks>
public sealed record CompanyMembersResponseV1(
    [property: JsonPropertyName("mitglieder")] IReadOnlyList<CompanyMemberV1> Members);
