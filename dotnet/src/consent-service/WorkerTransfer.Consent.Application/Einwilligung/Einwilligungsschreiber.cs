using Girder.Core.Identity;
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
public sealed class Einwilligungsschreiber(
    IConsentLedger buch,
    IAuditTrail pruefspur,
    IKorrelation korrelation,
    TimeProvider uhr)
{
    /// <summary>Records a grant.</summary>
    public Task<Einwilligungsergebnis> ErteileAsync(
        SubjectId handelnder,
        TenantId? firma,
        Guid gegenstand,
        string faehigkeit,
        string? grund,
        CancellationToken cancellationToken) =>
        SchreibeAsync(
            ConsentAction.Grant, handelnder, firma, gegenstand, faehigkeit, grund,
            cancellationToken);

    /// <summary>Records a withdrawal.</summary>
    public Task<Einwilligungsergebnis> WiderrufeAsync(
        SubjectId handelnder,
        TenantId? firma,
        Guid gegenstand,
        string faehigkeit,
        string grund,
        CancellationToken cancellationToken) =>
        SchreibeAsync(
            ConsentAction.Revoke, handelnder, firma, gegenstand, faehigkeit, grund,
            cancellationToken);

    private static AuditAction Pruefhandlung(ConsentAction handlung) => handlung switch
    {
        ConsentAction.Grant => AuditAction.ConsentGrant,
        ConsentAction.Revoke => AuditAction.ConsentRevoke,
        _ => AuditAction.ConsentDelete
    };

    private async Task<Einwilligungsergebnis> SchreibeAsync(
        ConsentAction handlung,
        SubjectId handelnder,
        TenantId? firma,
        Guid gegenstand,
        string faehigkeit,
        string? grund,
        CancellationToken cancellationToken)
    {
        // Before anything is read or written: a person manages their own
        // consent and nobody else's.
        if (handelnder.Value != gegenstand)
        {
            return new Einwilligungsergebnis.FremderGegenstand();
        }

        if (!Capability.TryParse(faehigkeit, out var faehigkeitswert))
        {
            return new Einwilligungsergebnis.Unbrauchbar("capability");
        }

        WithdrawalReason? grundwert = null;

        if (grund is not null && !WithdrawalReason.TryParse(grund, out grundwert))
        {
            return new Einwilligungsergebnis.Unbrauchbar("reason");
        }

        if (handlung is ConsentAction.Revoke && grundwert is null)
        {
            return new Einwilligungsergebnis.Unbrauchbar("reason");
        }

        var jetzt = uhr.GetUtcNow();
        var subject = new SubjectId(gegenstand);

        var ereignis = handlung is ConsentAction.Grant
            ? ConsentEvent.Grant(subject, faehigkeitswert, jetzt, handelnder, grundwert)
            : ConsentEvent.Revoke(subject, faehigkeitswert, jetzt, grundwert!, handelnder);

        await buch.AnhaengenAsync(ereignis, cancellationToken);

        // Same transaction as the fact above (ADR-0012). The capability name is
        // allowlisted metadata; the data it governs never is, and neither is
        // the reason the person wrote — that one stays in the ledger row, where
        // only they can read it back.
        await pruefspur.AnhaengenAsync(
            new AuditEvent(
                Pruefhandlung(handlung),
                jetzt,
                handelnder,
                firma,
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
        var neuestes = await buch.NeuestesAsync(subject, faehigkeitswert, cancellationToken);

        return new Einwilligungsergebnis.Stand(
            Projektion.Stand(neuestes is null ? [] : [neuestes]));
    }
}
