using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Messages;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Erasure;

/// <summary>Delete everything this service holds about one person.</summary>
/// <param name="Subject">The person, as the cascade named them.</param>
public sealed record ExecuteErasureCommand(Guid Subject) : ICommandMessage<ErasureReceipt>;

/// <summary>The receipt.</summary>
/// <param name="Retained">
/// Zero, always. This service has no retention exception: the two switches
/// ADR-0027 §3 knows of are a hired application and a paid transfer, and
/// neither stands here.
/// </param>
public sealed record ErasureReceipt(int Retained = 0);

/// <summary>
/// The recipient where something deliberately <em>stays</em> (ADR-0027 §5).
/// </summary>
/// <remarks>
/// The ledger is the <strong>proof</strong> that the erasure happened. Deleting
/// it along with everything else would make the promise unprovable — "we
/// deleted" with nothing left to check it against.
/// <para>
/// <strong>What stays:</strong> the whole chain of grants, withdrawals and, at
/// the end, one closing fact per capability — identifiers, capability names and
/// timestamps.
/// </para>
/// <para>
/// <strong>What goes:</strong> <c>reason</c> and <c>metadata</c>, in both
/// tables. The reason is free text a person wrote about themselves, and the
/// only genuinely personal field here. The proof does not need it: <em>that</em>
/// it was withdrawn is in the action.
/// </para>
/// <para>
/// <strong>Why that squares with the right to erasure, and where the argument
/// ends.</strong> It is not "we may keep things", but that what remains is no
/// longer information about a person: afterwards nothing in the system maps
/// <c>subject_id</c> to a human being — address, name and password hash lived
/// only in identity's <c>users</c>, and that row falls. In the default that
/// carries completely. A thrown retention switch elsewhere would keep the key
/// alive, and the rest would then be pseudonymous rather than anonymous.
/// </para>
/// </remarks>
public sealed class ExecuteErasureHandler(
    IConsentLedger ledger,
    IAuditTrail auditTrail,
    IFreeTextClearing clearing,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<ExecuteErasureCommand, ErasureReceipt>
{
    /// <inheritdoc />
    public async Task<ErasureReceipt> Handle(
        ExecuteErasureCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subject = new SubjectId(request.Subject);
        var now = clock.GetUtcNow();
        var latest = await ledger.LatestPerSkillAsync(subject, cancellationToken);
        var closed = 0;

        foreach (var consentEvent in latest)
        {
            // Already closed ones are skipped, and that is the idempotency
            // (ADR-0027 §4.2). Deleting is idempotent by nature, appending is
            // not: a second delivery would otherwise write a second closing
            // fact per capability. No harm, but an untruth — the person deleted
            // once, not twice.
            if (consentEvent.Action is ConsentAction.Delete)
            {
                continue;
            }

            // The person themselves. There is no delegation model, and that is
            // deliberate: nobody but they deletes their account.
            await ledger.AttachAsync(
                ConsentEvent.Delete(subject, consentEvent.Capability, now, subject),
                cancellationToken);
            closed++;
        }

        // Without the capability names. The trail records that the erasure ran;
        // listing what it covered would be the one statement about the person
        // that the very next step has to remove again.
        //
        // Only when this delivery closed something — the same idempotency as
        // the ledger above. A second delivery wrote a second `consent_delete`
        // until 03.10.2026: the trail then said the person was erased twice.
        if (closed > 0)
        {
            await auditTrail.AttachAsync(
                new AuditEvent(
                    AuditAction.ConsentDelete, now, subject, null, subject, correlation.Current),
                cancellationToken);
        }

        // Afterwards, not before: this way it also catches the rows just
        // written, and there stays exactly one place where "no free text any
        // more" is enforced instead of two that can drift apart.
        await clearing.ClearAsync(request.Subject, cancellationToken);

        return new ErasureReceipt();
    }
}
