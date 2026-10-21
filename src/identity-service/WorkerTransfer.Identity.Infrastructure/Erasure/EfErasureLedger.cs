using Noelia.Core.Identity;
using Noelia.Abstractions.Security.Sessions;
using Noelia.Infrastructure.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Infrastructure.Erasure;

/// <summary>identity's own share of an erasure.</summary>
public sealed class EfErasureLedger(IdentityDbContext context, ITokenSessionService sessions)
    : IErasureLedger
{
    /// <inheritdoc />
    public Task<bool> IsAlreadyRunningAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default) =>
        context.Set<OutboxRow>().AnyAsync(
            row => row.UserId == subject.Value
                     && row.Kind.StartsWith(ErasureRecipient.Prefix),
            cancellationToken);

    /// <inheritdoc />
    public async Task LockAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var account = await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(row => row.Id == subject.Value, cancellationToken);

        if (account is not null)
        {
            account.Status = AccountStatus.Disabled;
            account.UpdatedAt = now.UtcDateTime;
            account.Version += 1;
        }

        // Not only at the end: while the cascade runs, nobody may sign in under
        // this name any more. SignOutEverywhere runs over ExecuteUpdate and
        // joins the open transaction.
        await sessions.SignOutEverywhereAsync(subject, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> OpenIntentsAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        var open = await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value
                            && row.DeliveredAt == null
                            && row.Kind.StartsWith(ErasureRecipient.Prefix))
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);

        return open;
    }

    /// <inheritdoc />
    public async Task<ClosingAddress?> AddressAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Users
            .Where(entry => entry.Id == subject.Value)
            .Select(entry => new { entry.Email, entry.Language })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ClosingAddress(row.Email, LanguageChoice.From(row.Language));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantId>> CompleteAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var account = await context.Users
            .FirstOrDefaultAsync(row => row.Id == subject.Value, cancellationToken);

        if (account is null)
        {
            return [];
        }

        var affected = await context.Memberships
            .Where(row => row.UserId == subject.Value)
            .Select(row => row.TenantId)
            .ToListAsync(cancellationToken);

        // Invitations TO this address: they carry it in the clear.
        await context.Invitations
            .Where(row => row.Email == account.Email)
            .ExecuteDeleteAsync(cancellationToken);

        // audit_events stays, its metadata is cleared — no cascade with users,
        // and that is a deliberate decision (ADR-0012). The allowlist permits
        // ip and user_agent; nobody writes them today, but an erasure decides
        // the FORM, not the state of the day.
        await context.AuditEvents
            .Where(row => row.ActorId == subject.Value || row.TargetId == subject.Value)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.Metadata, "{}"),
                cancellationToken);

        // The row one forgets. Nothing points at users from here, so a plain
        // delete of the account leaves it standing.
        await context.Set<SessionCapacityRow>()
            .Where(row => context.RefreshTokens
                .Any(token => token.SessionId == row.SessionId
                              && token.SubjectId == subject.Value))
            .ExecuteDeleteAsync(cancellationToken);

        await context.RefreshTokens
            .Where(token => token.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // The settings — and with them the stored key.
        //
        // Expressly here and not over a foreign key: `subject_id` is the PRIMARY
        // KEY of this table and points at nothing, so no CASCADE clears it away.
        // Exactly the sort of row that stays standing after an erasure — and
        // this one would hold a secret.
        await context.AccountSettings
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // And now the deletion itself: afterwards nothing in this system maps a
        // subject id to a person. sessions, email_verification_tokens and
        // user_tenant_memberships fall with it through ON DELETE CASCADE;
        // company_invitations.invited_by becomes NULL.
        await context.Users
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        var admin = MembershipRoleNames.ToDatabase(MembershipRole.Admin);
        var dormant = new List<TenantId>();

        foreach (var company in affected)
        {
            var stillHasOne = await context.Memberships.AnyAsync(
                row => row.TenantId == company && row.Role == admin, cancellationToken);

            if (stillHasOne)
            {
                continue;
            }

            await context.Tenants
                .Where(row => row.Id == company)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(row => row.Status, "dormant"),
                    cancellationToken);

            // Nobody is left who may use or change the company's AI key, and a
            // tenant is never deleted — so no cascade would ever take it.
            await context.CompanyAiSettings
                .Where(row => row.TenantId == company)
                .ExecuteDeleteAsync(cancellationToken);

            dormant.Add(new TenantId(company));
        }

        return dormant;
    }
}
