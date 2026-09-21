using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>What a signed-in person is told about themselves.</summary>
/// <param name="Subject">Their subject id.</param>
/// <param name="Email">
/// Their address. Read from the account rather than carried in the token: a JWT
/// travels with every single request and is no place for an address.
/// </param>
/// <param name="Company">The company they currently act for, or <c>null</c> for themselves.</param>
/// <param name="OccupationalField">
/// Which working world they stand in, or <c>null</c>. It travels along so the
/// interface can decide what to offer — not in the token (ADR-0039).
/// </param>
/// <remarks>
/// Carries no roles. Nothing reads them — every permission decision in this
/// system reads <c>user_tenant_memberships</c> per operation — and a field
/// nobody reads is a field that goes stale and is then believed.
/// </remarks>
public sealed record AccountView(
    SubjectId Subject,
    string? Email,
    TenantId? Company,
    AccountLanguage Language,
    string DisplayName,
    string? GivenName = null,
    string? FamilyName = null,
    OccupationalField? OccupationalField = null);
