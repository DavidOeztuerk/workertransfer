using Girder.Core.Identity;

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
    void Bestaetigungslink(string an, SubjectId empfaenger, string klartextToken);

    /// <summary>
    /// "Somebody tried to register with your address."
    /// </summary>
    /// <remarks>
    /// Goes to the real owner rather than telling the asker anything. It is the
    /// only thing that happens on a duplicate registration, and it is why the
    /// endpoint can answer identically in both cases.
    /// </remarks>
    void Doppelanmeldung(string an, SubjectId empfaenger);
}
