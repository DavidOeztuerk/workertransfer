using Girder.Core.Identity;
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
            .FirstOrDefaultAsync(kandidat => kandidat.TokenHash == tokenHash, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<Invitation?> FindAsync(
        TenantId tenant,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Invitations.FirstOrDefaultAsync(
            kandidat => kandidat.Id == id && kandidat.TenantId == tenant.Value,
            cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Invitation>> ListOpenAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        var offen = InvitationStatusNames.ToDatabase(InvitationStatus.Pending);

        var zeilen = await context.Invitations
            .Where(kandidat => kandidat.TenantId == tenant.Value && kandidat.Status == offen)
            .OrderBy(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
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
            .FirstOrDefaultAsync(kandidat => kandidat.Id == invitation.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Keine Einladung {invitation.Id} zum Speichern.");

        row.Status = InvitationStatusNames.ToDatabase(invitation.Status);
        row.AcceptedAt = invitation.AcceptedAt?.UtcDateTime;
    }

    private static Invitation ZumAggregat(InvitationRow row) => Invitation.Restore(
        row.Id,
        new TenantId(row.TenantId),
        row.Email,
        MembershipRoleNames.FromDatabase(row.Role),
        row.InvitedBy is { } wer ? new SubjectId(wer) : null,
        InvitationStatusNames.FromDatabase(row.Status),
        new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero),
        row.AcceptedAt is { } angenommen ? new DateTimeOffset(angenommen, TimeSpan.Zero) : null);
}
