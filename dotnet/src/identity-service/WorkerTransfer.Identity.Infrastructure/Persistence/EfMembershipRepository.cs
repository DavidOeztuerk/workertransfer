using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads <c>user_tenant_memberships</c>.</summary>
public sealed class EfMembershipRepository(IdentityDbContext context, TimeProvider uhr)
    : IMembershipRepository
{
    /// <inheritdoc />
    public Task<bool> IsMemberAsync(
        SubjectId subject,
        TenantId tenant,
        CancellationToken cancellationToken = default) =>
        context.Memberships.AnyAsync(
            row => row.UserId == subject.Value && row.TenantId == tenant.Value,
            cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(
        SubjectId subject,
        TenantId tenant,
        MembershipRole role,
        CancellationToken cancellationToken = default)
    {
        context.Memberships.Add(new MembershipRow
        {
            Id = Guid.CreateVersion7(),
            UserId = subject.Value,
            TenantId = tenant.Value,
            Role = MembershipRoleNames.ToDatabase(role),
            GrantedAt = uhr.GetUtcNow().UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<MembershipRole?> RoleOfAsync(
        SubjectId subject,
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        var gespeichert = await context.Memberships
            .Where(row => row.UserId == subject.Value && row.TenantId == tenant.Value)
            .Select(row => row.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return gespeichert is null ? null : MembershipRoleNames.FromDatabase(gespeichert);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mitgliedschaft>> ListForSubjectAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        var zeilen = await context.Memberships
            .Where(row => row.UserId == subject.Value)
            .Join(context.Tenants,
                mitglied => mitglied.TenantId,
                firma => firma.Id,
                (mitglied, firma) => new { firma.Id, firma.Name, firma.Domain, mitglied.Role })
            .OrderBy(eintrag => eintrag.Name)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(eintrag => new Mitgliedschaft(
            new TenantId(eintrag.Id),
            eintrag.Name,
            eintrag.Domain,
            MembershipRoleNames.FromDatabase(eintrag.Role)))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Firmenmitglied>> ListMembersAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        var zeilen = await context.Memberships
            .Where(row => row.TenantId == tenant.Value)
            .Join(context.Users,
                mitglied => mitglied.UserId,
                benutzer => benutzer.Id,
                (mitglied, benutzer) => new { benutzer.Id, benutzer.DisplayName, mitglied.Role })
            .OrderBy(eintrag => eintrag.DisplayName)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(eintrag => new Firmenmitglied(
            new SubjectId(eintrag.Id),
            eintrag.DisplayName,
            MembershipRoleNames.FromDatabase(eintrag.Role)))];
    }

    /// <inheritdoc />
    public Task<int> CountAdminsAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        var admin = MembershipRoleNames.ToDatabase(MembershipRole.Admin);

        return context.Memberships.CountAsync(
            row => row.TenantId == tenant.Value && row.Role == admin, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Through the change tracker rather than <c>ExecuteDeleteAsync</c>: the
    /// latter runs immediately, and the row would stay gone even if the command
    /// around it later failed.
    /// </remarks>
    public async Task RemoveAsync(
        SubjectId subject,
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        var zeilen = await context.Memberships
            .AsTracking()
            .Where(row => row.UserId == subject.Value && row.TenantId == tenant.Value)
            .ToListAsync(cancellationToken);

        context.Memberships.RemoveRange(zeilen);
    }
}
