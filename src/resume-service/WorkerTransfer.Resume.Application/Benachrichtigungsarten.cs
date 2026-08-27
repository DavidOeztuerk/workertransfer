namespace WorkerTransfer.Resume.Application;

/// <summary>The kinds this service puts into its outbox.</summary>
/// <remarks>
/// A kind is a <em>reference</em>, never a document: the recipient reads the
/// current state itself. That is what lets the table carry nothing but
/// <c>user_id</c> and <c>kind</c>, and it is why a redelivery is harmless
/// (ADR-0025).
/// </remarks>
public static class Benachrichtigungsarten
{
    /// <summary>A company asked. Goes to the person asked.</summary>
    public const string Angefragt = "resume.requested";

    /// <summary>The person said yes. Goes to whoever asked.</summary>
    public const string Erteilt = "resume.granted";

    /// <summary>The person said no. Goes to whoever asked.</summary>
    public const string Abgelehnt = "resume.declined";

    // There is deliberately no kind for a withdrawal.
    //
    // A grant and a refusal are the answer to a question the company itself
    // asked, and it is entitled to hear it. A withdrawal is not: pushing it at
    // the company would turn taking a release back into a confrontation, and
    // the whole point of the ledger being read fresh on every request is that
    // withdrawing costs the person nothing. The company notices the same way it
    // would anyway — the next read comes up empty.
}
