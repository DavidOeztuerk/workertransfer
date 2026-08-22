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
}
