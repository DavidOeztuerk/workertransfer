using Noelia.Core.Identity;

namespace WorkerTransfer.Notification.Application.Ports;

/// <summary>Asks identity-service to send the one sentence out.</summary>
/// <remarks>
/// <strong>This service knows no email address, and that is the whole
/// design.</strong> The Python service left the notifications in
/// identity-service for exactly this reason: "a <c>notifications-service</c>
/// would need the email address; copying it there, or handing it out over a
/// <c>subject_id → email</c> lookup, would mean multiplying the most sensitive
/// datum in the system — for a text mail."
/// <para>
/// The objection holds, and it is not answered by ignoring it but by moving
/// <em>the other half</em>. Here lies <em>whether</em> something goes out: the
/// switches and the throttle. There lies <em>to whom</em>: the address
/// identity-service has anyway. Across the boundary goes only a <c>user_id</c>,
/// which goes everywhere else already.
/// </para>
/// <para>
/// And the kind does <strong>not</strong> travel with it. identity-service
/// phrases the one fixed sentence; a caller that could send the kind along
/// would be the beginning of "just this one line as well". The Python
/// service's rule — the caller has no access to the text — holds here across a
/// service boundary and is therefore stronger, not weaker.
/// </para>
/// </remarks>
public interface IMailer
{
    /// <summary>"Tell this person there is something new."</summary>
    /// <remarks>
    /// Does not throw. A failure here must never topple the case that triggered
    /// it — with consent it is about permission (in doubt, no), here about
    /// courtesy.
    /// </remarks>
    Task SendAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
