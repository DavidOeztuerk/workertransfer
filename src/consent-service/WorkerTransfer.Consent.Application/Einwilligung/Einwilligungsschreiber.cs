using Noelia.Core.Identity;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>The one way a fact and its audit row come into being.</summary>
/// <remarks>
/// Granting and withdrawing differ in two places — the factory that is called
/// and whether a reason is mandatory — and agree in everything else: who may
/// do it, what is recorded alongside, what is answered. One writer keeps the
/// agreement true rather than duplicated.
/// </remarks>
public sealed class ConsentWriter(
    IConsentLedger buch,
    IAuditTrail pruefspur,
    IKorrelation korrelation,
    TimeProvider uhr)
{
    /// <summary>Records a grant.</summary>
    public Task<ConsentResult> GrantAsync(
        SubjectId handelnder,
        TenantId? company,
        Guid gegenstand,
        string skill,
        string? reason,
        CancellationToken cancellationToken) =>
        WriteAsync(
            ConsentAction.Grant, handelnder, company, gegenstand, skill, reason,
            cancellationToken);

    /// <summary>Records a withdrawal.</summary>
    public Task<ConsentResult> RevokeAsync(
        SubjectId handelnder,
        TenantId? company,
        Guid gegenstand,
        string skill,
        string reason,
        CancellationToken cancellationToken) =>
        WriteAsync(
            ConsentAction.Revoke, handelnder, company, gegenstand, skill, reason,
            cancellationToken);

    private static AuditAction ToAuditAction(ConsentAction action) => action switch
    {
        ConsentAction.Grant => AuditAction.ConsentGrant,
        ConsentAction.Revoke => AuditAction.ConsentRevoke,
        _ => AuditAction.ConsentDelete
    };

    private async Task<ConsentResult> WriteAsync(
        ConsentAction action,
        SubjectId handelnder,
        TenantId? company,
        Guid gegenstand,
        string skill,
        string? reason,
        CancellationToken cancellationToken)
    {
        // Before anything is read or written: a person manages their own
        // consent and nobody else's.
        if (handelnder.Value != gegenstand)
        {
            return new ConsentResult.ForeignSubject();
        }

        if (!Capability.TryParse(skill, out var faehigkeitswert))
        {
            return new ConsentResult.Unbrauchbar("capability");
        }

        WithdrawalReason? grundwert = null;

        if (reason is not null && !WithdrawalReason.TryParse(reason, out grundwert))
        {
            return new ConsentResult.Unbrauchbar("reason");
        }

        if (action is ConsentAction.Revoke && grundwert is null)
        {
            return new ConsentResult.Unbrauchbar("reason");
        }

        var now = uhr.GetUtcNow();
        var subject = new SubjectId(gegenstand);

        var ereignis = action is ConsentAction.Grant
            ? ConsentEvent.Grant(subject, faehigkeitswert, now, handelnder, grundwert)
            : ConsentEvent.Revoke(subject, faehigkeitswert, now, grundwert!, handelnder);

        await buch.AttachAsync(ereignis, cancellationToken);

        // Same transaction as the fact above (ADR-0012). The capability name is
        // allowlisted metadata; the data it governs never is, and neither is
        // the reason the person wrote — that one stays in the ledger row, where
        // only they can read it back.
        await pruefspur.AttachAsync(
            new AuditEvent(
                ToAuditAction(action),
                now,
                handelnder,
                company,
                subject,
                korrelation.Aktuell,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["capability"] = faehigkeitswert.Value
                }),
            cancellationToken);

        // Read back rather than assumed: the fact just written is the newest
        // one unless a clock went backwards, and in that case the ledger is
        // right and this handler is not.
        var neuestes = await buch.LatestOneAsync(subject, faehigkeitswert, cancellationToken);

        return new ConsentResult.State(
            Projektion.State(neuestes is null ? [] : [neuestes]));
    }
}
