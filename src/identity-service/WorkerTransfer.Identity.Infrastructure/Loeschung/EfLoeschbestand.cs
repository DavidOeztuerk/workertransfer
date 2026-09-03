using Girder.Core.Identity;
using Girder.Infrastructure.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Loeschung;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Infrastructure.Loeschung;

/// <summary>identity's own share of an erasure.</summary>
public sealed class EfLoeschbestand(IdentityDbContext kontext, ITokenSessionService sitzungen)
    : ILoeschbestand
{
    /// <inheritdoc />
    public Task<bool> LaeuftBereitsAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default) =>
        kontext.Set<OutboxZeile>().AnyAsync(
            zeile => zeile.UserId == wer.Value
                     && zeile.Kind.StartsWith(Loeschempfaenger.Praefix),
            cancellationToken);

    /// <inheritdoc />
    public async Task SperreAsync(
        SubjectId wer,
        DateTimeOffset jetzt,
        CancellationToken cancellationToken = default)
    {
        var konto = await kontext.Users
            .AsTracking()
            .FirstOrDefaultAsync(zeile => zeile.Id == wer.Value, cancellationToken);

        if (konto is not null)
        {
            konto.Status = AccountStatus.Disabled;
            konto.UpdatedAt = jetzt.UtcDateTime;
            konto.Version += 1;
        }

        // Not only at the end: while the cascade runs, nobody may sign in under
        // this name any more. SignOutEverywhere runs over ExecuteUpdate and
        // joins the open transaction.
        await sitzungen.SignOutEverywhereAsync(wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> OffeneAbsichtenAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default)
    {
        var offene = await kontext.Set<OutboxZeile>()
            .Where(zeile => zeile.UserId == wer.Value
                            && zeile.DeliveredAt == null
                            && zeile.Kind.StartsWith(Loeschempfaenger.Praefix))
            .OrderBy(zeile => zeile.CreatedAt)
            .Select(zeile => zeile.Kind)
            .ToListAsync(cancellationToken);

        return offene;
    }

    /// <inheritdoc />
    public async Task<Schlussanschrift?> AdresseAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Users
            .Where(eintrag => eintrag.Id == wer.Value)
            .Select(eintrag => new { eintrag.Email, eintrag.Language })
            .FirstOrDefaultAsync(cancellationToken);

        return zeile is null
            ? null
            : new Schlussanschrift(zeile.Email, Sprachwahl.Aus(zeile.Language));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantId>> SchliesseAbAsync(
        SubjectId wer,
        DateTimeOffset jetzt,
        CancellationToken cancellationToken = default)
    {
        var konto = await kontext.Users
            .FirstOrDefaultAsync(zeile => zeile.Id == wer.Value, cancellationToken);

        if (konto is null)
        {
            return [];
        }

        var betroffene = await kontext.Memberships
            .Where(zeile => zeile.UserId == wer.Value)
            .Select(zeile => zeile.TenantId)
            .ToListAsync(cancellationToken);

        // Invitations TO this address: they carry it in the clear.
        await kontext.Invitations
            .Where(zeile => zeile.Email == konto.Email)
            .ExecuteDeleteAsync(cancellationToken);

        // audit_events stays, its metadata is cleared — no cascade with users,
        // and that is a deliberate decision (ADR-0012). The allowlist permits
        // ip and user_agent; nobody writes them today, but an erasure decides
        // the FORM, not the state of the day.
        await kontext.AuditEvents
            .Where(zeile => zeile.ActorId == wer.Value || zeile.TargetId == wer.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(zeile => zeile.Metadata, "{}"),
                cancellationToken);

        // The row one forgets. Nothing points at users from here, so a plain
        // delete of the account leaves it standing.
        await kontext.Set<SessionCapacityRow>()
            .Where(zeile => kontext.RefreshTokens
                .Any(token => token.SessionId == zeile.SessionId
                              && token.SubjectId == wer.Value))
            .ExecuteDeleteAsync(cancellationToken);

        await kontext.RefreshTokens
            .Where(token => token.SubjectId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // And now the deletion itself: afterwards nothing in this system maps a
        // subject id to a person. sessions, email_verification_tokens and
        // user_tenant_memberships fall with it through ON DELETE CASCADE;
        // company_invitations.invited_by becomes NULL.
        await kontext.Users
            .Where(zeile => zeile.Id == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        var admin = MembershipRoleNames.ToDatabase(MembershipRole.Admin);
        var stillgelegt = new List<TenantId>();

        foreach (var firma in betroffene)
        {
            var hatNoch = await kontext.Memberships.AnyAsync(
                zeile => zeile.TenantId == firma && zeile.Role == admin, cancellationToken);

            if (hatNoch)
            {
                continue;
            }

            await kontext.Tenants
                .Where(zeile => zeile.Id == firma)
                .ExecuteUpdateAsync(
                    setzen => setzen.SetProperty(zeile => zeile.Status, "dormant"),
                    cancellationToken);

            stillgelegt.Add(new TenantId(firma));
        }

        return stillgelegt;
    }
}
