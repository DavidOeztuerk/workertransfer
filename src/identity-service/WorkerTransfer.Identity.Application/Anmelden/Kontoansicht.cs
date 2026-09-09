using Girder.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>What a signed-in person is told about themselves.</summary>
/// <param name="Wer">Their subject id.</param>
/// <param name="Email">
/// Their address. Read from the account rather than carried in the token: a JWT
/// travels with every single request and is no place for an address.
/// </param>
/// <param name="Firma">The company they currently act for, or <c>null</c> for themselves.</param>
/// <remarks>
/// Carries no roles. Nothing reads them — every permission decision in this
/// system reads <c>user_tenant_memberships</c> per operation — and a field
/// nobody reads is a field that goes stale and is then believed.
/// </remarks>
public sealed record Kontoansicht(
    SubjectId Wer,
    string? Email,
    TenantId? Firma,
    Kontosprache Sprache,
    string Anzeigename,
    string? Vorname = null,
    string? Nachname = null);
