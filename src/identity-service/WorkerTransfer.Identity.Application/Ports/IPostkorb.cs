using Girder.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Mail a command wants sent once its transaction has committed.</summary>
/// <remarks>
/// Emphatically <em>not</em> the durable outbox of ADR-0025. That one records an
/// intent in the same transaction and holds no content — precisely so nothing
/// ends up in every backup. A confirmation mail carries a token whose plaintext
/// exists in exactly two places, the mail and this request; writing it to a
/// table would give up the reason for hashing it.
/// <para>
/// So: collected in memory, sent after the commit, and a failure changes
/// nothing that was written. The repair is "send it again", which is a button
/// the person already has.
/// </para>
/// <para>
/// Named intents rather than subject-and-body, so the wording and the link's
/// base address stay out of the application layer — and so the two mails this
/// service sends are two things one can find, not three call sites assembling
/// strings.
/// </para>
/// <para>
/// <strong>Every intent carries the recipient's language</strong>, and it comes
/// from the account rather than the request. Two of these mails are written
/// without a request at all — the deletion confirmation when the last of eight
/// services acknowledges, the news mail from a dispatcher — so an
/// <c>Accept-Language</c> would be absent exactly where it is needed
/// (<see cref="Kontosprache"/>).
/// </para>
/// </remarks>
public interface IPostkorb
{
    /// <summary>"Confirm your address", with the link.</summary>
    /// <param name="an">Where to send it.</param>
    /// <param name="empfaenger">
    /// Whose account it is. For the log, so a failure does not write the
    /// address into it.
    /// </param>
    /// <param name="klartextToken">The token, in the clear. Only ever here and in the mail.</param>
    /// <param name="sprache">What to write in.</param>
    void Bestaetigungslink(
        string an, SubjectId empfaenger, string klartextToken, Kontosprache sprache);

    /// <summary>
    /// "Somebody tried to register with your address."
    /// </summary>
    /// <remarks>
    /// Goes to the real owner rather than telling the asker anything. It is the
    /// only thing that happens on a duplicate registration, and it is why the
    /// endpoint can answer identically in both cases.
    /// </remarks>
    /// <param name="an">Where to send it.</param>
    /// <param name="empfaenger">Whose account it is.</param>
    /// <param name="sprache">What to write in.</param>
    void Doppelanmeldung(string an, SubjectId empfaenger, Kontosprache sprache);

    /// <summary>
    /// "You have been invited into a company", with the link.
    /// </summary>
    /// <remarks>
    /// Goes to an address, which may or may not have an account here — and the
    /// mail must not differ between the two cases, or it becomes a way to ask
    /// about platform membership without asking the consent ledger.
    /// </remarks>
    /// <param name="an">The invited address.</param>
    /// <param name="einladender">
    /// Who invited. For the log; the mail names the company, not the person.
    /// </param>
    /// <param name="firma">Which company, so the recipient can recognise it.</param>
    /// <param name="klartextToken">The token, in the clear. Only ever here and in the mail.</param>
    /// <param name="sprache">
    /// What to write in. Taken from the <em>inviting</em> account when the
    /// address is unknown here — an invitation goes to somebody who may have no
    /// account, so there is no row of theirs to read.
    /// </param>
    void Einladung(
        string an, SubjectId einladender, string firma, string klartextToken,
        Kontosprache sprache);

    /// <summary>"Your account is gone."</summary>
    /// <remarks>
    /// Says only <em>that</em> it is done. Listing what was deleted would copy
    /// the data into an inbox that may not be the person's alone (ADR-0027 §6)
    /// — and it would be the one place where a deletion produced a new record.
    /// </remarks>
    /// <param name="an">
    /// The address, read at delivery time from the row that is about to fall.
    /// Never from the outbox: that is durable storage.
    /// </param>
    /// <param name="wer">Whose account it was. For the log.</param>
    /// <param name="sprache">
    /// What to write in — read from the row before it falls. This mail is the
    /// last thing that account ever receives, and it is written long after the
    /// request that asked for the deletion.
    /// </param>
    void Loeschbestaetigung(string an, SubjectId wer, Kontosprache sprache);

    /// <summary>"There is something new for you." Nothing more.</summary>
    /// <remarks>
    /// The same sentence for every kind, and the kind is not a parameter — it
    /// is precisely the secret. A mail lands in a mailbox, and that mailbox may
    /// be the one at the current employer: on their servers, in their backups,
    /// in the view of their administrators. A line like "Acme GmbH would like to
    /// see your market status" is exactly the disclosure this platform is built
    /// against, sent voluntarily and in the clear.
    /// <para>
    /// notification-service decides <em>whether</em> this goes out — it owns the
    /// four switches and the throttle. It cannot decide <em>what</em> it says,
    /// because it cannot pass anything but a subject id. That is the guarantee
    /// the Python service had inside one process, kept across a service
    /// boundary.
    /// </para>
    /// </remarks>
    /// <param name="an">Where to send it.</param>
    /// <param name="wer">Whose account it is. For the log.</param>
    /// <param name="sprache">What to write in.</param>
    void Neuigkeit(string an, SubjectId wer, Kontosprache sprache);
}
