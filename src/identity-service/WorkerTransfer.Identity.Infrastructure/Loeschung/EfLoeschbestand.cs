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
                     && row.Kind.StartsWith(ErasureRecipient.Praefix),
            cancellationToken);

    /// <inheritdoc />
    public async Task LockAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var konto = await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(row => row.Id == subject.Value, cancellationToken);

        if (konto is not null)
        {
            konto.Status = AccountStatus.Disabled;
            konto.UpdatedAt = now.UtcDateTime;
            konto.Version += 1;
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
        var offene = await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value
                            && row.DeliveredAt == null
                            && row.Kind.StartsWith(ErasureRecipient.Praefix))
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);

        return offene;
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
            : new ClosingAddress(row.Email, Sprachwahl.Aus(row.Language));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantId>> CompleteAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var konto = await context.Users
            .FirstOrDefaultAsync(row => row.Id == subject.Value, cancellationToken);

        if (konto is null)
        {
            return [];
        }

        var betroffene = await context.Memberships
            .Where(row => row.UserId == subject.Value)
            .Select(row => row.TenantId)
            .ToListAsync(cancellationToken);

        // Invitations TO this address: they carry it in the clear.
        await context.Invitations
            .Where(row => row.Email == konto.Email)
            .ExecuteDeleteAsync(cancellationToken);

        // audit_events stays, its metadata is cleared — no cascade with users,
        // and that is a deliberate decision (ADR-0012). The allowlist permits
        // ip and user_agent; nobody writes them today, but an erasure decides
        // the FORM, not the state of the day.
        await context.AuditEvents
            .Where(row => row.ActorId == subject.Value || row.TargetId == subject.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(row => row.Metadata, "{}"),
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

        // Die Einstellungen — und mit ihnen der hinterlegte Schlüssel.
        //
        // Ausdrücklich hier und nicht über einen Fremdschlüssel: `subject_id`
        // ist der PRIMÄRSCHLÜSSEL dieser Tabelle und zeigt auf nichts, also
        // räumt kein CASCADE sie mit weg. Genau die Sorte Zeile, die nach einer
        // Löschung stehen bleibt — und diese hier hielte ein Geheimnis.
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
        var stillgelegt = new List<TenantId>();

        foreach (var company in betroffene)
        {
            var hatNoch = await context.Memberships.AnyAsync(
                row => row.TenantId == company && row.Role == admin, cancellationToken);

            if (hatNoch)
            {
                continue;
            }

            await context.Tenants
                .Where(row => row.Id == company)
                .ExecuteUpdateAsync(
                    setzen => setzen.SetProperty(row => row.Status, "dormant"),
                    cancellationToken);

            stillgelegt.Add(new TenantId(company));
        }

        return stillgelegt;
    }
}
