using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads and writes <c>company_invitations</c>.</summary>
public sealed class EfInvitationRepository(IdentityDbContext context) : IInvitationRepository
{
    /// <inheritdoc />
    public Task AddAsync(
        Invitation invitation,
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        context.Invitations.Add(new InvitationRow
        {
            Id = invitation.Id,
            TenantId = invitation.Tenant.Value,
            Email = invitation.Email,
            Role = MembershipRoleNames.ToDatabase(invitation.Role),
            InvitedBy = invitation.InvitedBy?.Value,
            Status = InvitationStatusNames.ToDatabase(invitation.Status),
            TokenHash = tokenHash,
            CreatedAt = invitation.CreatedAt.UtcDateTime,
            ExpiresAt = invitation.ExpiresAt.UtcDateTime,
            AcceptedAt = invitation.AcceptedAt?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Invitation?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Invitations
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<Invitation?> FindAsync(
        TenantId tenant,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Invitations.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.TenantId == tenant.Value,
            cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Invitation>> ListOpenAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        var open = InvitationStatusNames.ToDatabase(InvitationStatus.Pending);

        var rows = await context.Invitations
            .Where(candidate => candidate.TenantId == tenant.Value && candidate.Status == open)
            .OrderBy(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only what an aggregate can change: the status and the moment it was
    /// taken up. Neither the address nor the token hash has a way to change on
    /// it, so writing them back would only be a chance to write them back
    /// wrong.
    /// </remarks>
    public async Task SaveAsync(
        Invitation invitation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        var row = await context.Invitations
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == invitation.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No invitation {invitation.Id} to save.");

        row.Status = InvitationStatusNames.ToDatabase(invitation.Status);
        row.AcceptedAt = invitation.AcceptedAt?.UtcDateTime;
    }

    private static Invitation ToAggregate(InvitationRow row) => Invitation.Restore(
        row.Id,
        new TenantId(row.TenantId),
        row.Email,
        MembershipRoleNames.FromDatabase(row.Role),
        row.InvitedBy is { } subject ? new SubjectId(subject) : null,
        InvitationStatusNames.FromDatabase(row.Status),
        new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero),
        row.AcceptedAt is { } acceptedAt ? new DateTimeOffset(acceptedAt, TimeSpan.Zero) : null);
}
