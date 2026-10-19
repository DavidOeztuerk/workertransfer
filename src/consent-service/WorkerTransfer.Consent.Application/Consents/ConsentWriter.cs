using Noelia.Core.Identity;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Consents;

/// <summary>The one way a fact and its audit row come into being.</summary>
/// <remarks>
/// Granting and withdrawing differ in two places — the factory that is called
/// and whether a reason is mandatory — and agree in everything else: who may
/// do it, what is recorded alongside, what is answered. One writer keeps the
/// agreement true rather than duplicated.
/// </remarks>
public sealed class ConsentWriter(
    IConsentLedger ledger,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
{
    /// <summary>Records a grant.</summary>
    public Task<ConsentResult> GrantAsync(
        SubjectId actor,
        TenantId? company,
        Guid subjectId,
        string skill,
        string? reason,
        CancellationToken cancellationToken) =>
        WriteAsync(
            ConsentAction.Grant, actor, company, subjectId, skill, reason,
            cancellationToken);

    /// <summary>Records a withdrawal.</summary>
    public Task<ConsentResult> RevokeAsync(
        SubjectId actor,
        TenantId? company,
        Guid subjectId,
        string skill,
        string reason,
        CancellationToken cancellationToken) =>
        WriteAsync(
            ConsentAction.Revoke, actor, company, subjectId, skill, reason,
            cancellationToken);

    private static AuditAction ToAuditAction(ConsentAction action) => action switch
    {
        ConsentAction.Grant => AuditAction.ConsentGrant,
        ConsentAction.Revoke => AuditAction.ConsentRevoke,
        _ => AuditAction.ConsentDelete
    };

    private async Task<ConsentResult> WriteAsync(
        ConsentAction action,
        SubjectId actor,
        TenantId? company,
        Guid subjectId,
        string skill,
        string? reason,
        CancellationToken cancellationToken)
    {
        // Before anything is read or written: a person manages their own
        // consent and nobody else's.
        if (actor.Value != subjectId)
        {
            return new ConsentResult.ForeignSubject();
        }

        if (!Capability.TryParse(skill, out var capabilityValue))
        {
            return new ConsentResult.Malformed("capability");
        }

        WithdrawalReason? reasonValue = null;

        if (reason is not null && !WithdrawalReason.TryParse(reason, out reasonValue))
        {
            return new ConsentResult.Malformed("reason");
        }

        if (action is ConsentAction.Revoke && reasonValue is null)
        {
            return new ConsentResult.Malformed("reason");
        }

        var now = clock.GetUtcNow();
        var subject = new SubjectId(subjectId);

        var consentEvent = action is ConsentAction.Grant
            ? ConsentEvent.Grant(subject, capabilityValue, now, actor, reasonValue)
            : ConsentEvent.Revoke(subject, capabilityValue, now, reasonValue!, actor);

        await ledger.AttachAsync(consentEvent, cancellationToken);

        // Same transaction as the fact above (ADR-0012). The capability name is
        // allowlisted metadata; the data it governs never is, and neither is
        // the reason the person wrote — that one stays in the ledger row, where
        // only they can read it back.
        await auditTrail.AttachAsync(
            new AuditEvent(
                ToAuditAction(action),
                now,
                actor,
                company,
                subject,
                correlation.Current,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["capability"] = capabilityValue.Value
                }),
            cancellationToken);

        // Read back rather than assumed: the fact just written is the newest
        // one unless a clock went backwards, and in that case the ledger is
        // right and this handler is not.
        var newest = await ledger.LatestOneAsync(subject, capabilityValue, cancellationToken);

        return new ConsentResult.State(
            Projection.State(newest is null ? [] : [newest]));
    }
}
