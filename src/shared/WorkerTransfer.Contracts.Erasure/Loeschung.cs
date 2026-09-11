using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Erasure;

/// <summary>"Delete everything you hold about this person."</summary>
/// <remarks>
/// One identifier, nothing else. That is not thrift, it is the thing itself: a
/// deletion order <em>has</em> no content. Which is exactly why it fits an
/// outbox whose table carries only <c>user_id</c> and <c>kind</c> (ADR-0025 §5)
/// — there the narrow contract is a stroke of luck rather than a compromise.
/// <para>
/// No reason field. Demanding a justification from somebody who wants to leave
/// is a lever against them (ADR-0027) — and the free text would be the one
/// thing that afterwards had to be deleted again.
/// </para>
/// <para>
/// The same body to every recipient. What it means for one service, that
/// service knows; the origin does not prescribe it, or identity-service would
/// have to know which tables stand elsewhere (ADR-0004).
/// </para>
/// </remarks>
public sealed record LoeschungV1([property: JsonPropertyName("user_id")] Guid UserId);

/// <summary>The receipt.</summary>
/// <param name="Retained">
/// Zero in the default, always.
/// </param>
/// <remarks>
/// ADR-0027 §3: the default deletes completely, including hired applications and
/// paid transfers. Only a thrown retention switch leaves anything standing — and
/// then the origin should be told rather than left to guess. Suspended is not
/// skipped.
/// </remarks>
public sealed record LoeschungsquittungV1(int Retained = 0);

/// <summary>"Withdraw this company's adverts."</summary>
/// <remarks>
/// The last-administrator case (ADR-0027 §7) — an intent about a
/// <em>company</em>, not about a person, and therefore a contract of its own
/// with a tenant rather than a user. It expressly does <em>not</em> count
/// toward the completeness proof of an erasure: otherwise a silent jobs-service
/// would hold a person's erasure open.
/// </remarks>
public sealed record UnternehmensrueckzugV1([property: JsonPropertyName("tenant_id")] Guid TenantId);

/// <summary>Who has to acknowledge before an erasure is finished.</summary>
public static class Loeschempfaenger
{
    /// <summary>The prefix that marks an outbox row as part of a cascade.</summary>
    public const string Praefix = "erasure:";

    /// <summary>
    /// The services that hold something about a natural person.
    /// </summary>
    /// <remarks>
    /// Not jobs-service and not companies-service: they hold nothing personal
    /// (ADR-0027 §2), and a deletion order to a service with nothing to delete
    /// would be an endpoint that says "done" without ever doing anything.
    /// <para>
    /// <c>github</c> is on this list, and the reason it was once left off is
    /// worth keeping: ADR-0022 deleted the <em>package</em>
    /// <c>worker-github</c>, which scored people. The <em>service</em> was
    /// built under that same ADR and does the opposite — it shows what GitHub
    /// says about a repository and derives nothing about a person. Confusing
    /// the two costs a service; the domain module says so in its first
    /// paragraph.
    /// </para>
    /// <para>
    /// <c>notification</c> is here for the same reason as everyone else: it
    /// holds the notification preferences, and those are keyed by the person. A
    /// service that holds a row per person is a recipient — that is the whole
    /// rule, and it is why this list is checked against the schemas rather than
    /// agreed on.
    /// </para>
    /// <para>
    /// <c>scout</c> joined with its first table (ADR-0036). It holds saved
    /// <em>searches</em> — the filters, never a result — and each belongs to
    /// the person who saved it. Its outbox rows carry a user id as well. A
    /// service that holds a row per person is a recipient; that is the whole
    /// rule.
    /// </para>
    /// <para>
    /// <c>advisor</c> joined with its first table (ADR-0037). It holds a
    /// <em>mandate</em> keyed by the person and the conversations they are in —
    /// and nothing about visibility, which lives in the ledger. Its outbox rows
    /// carry a user id as well. A service that holds a row per person is a
    /// recipient; that is the whole rule.
    /// </para>
    /// <para>
    /// A test goes red the moment any service grows a table with
    /// <c>subject_id</c> or <c>user_id</c> and is not on this list.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Fremde { get; } =
    [
        "consent", "profile", "resume", "portfolio",
        "applications", "transfer", "github", "notification", "scout", "advisor"
    ];

    /// <summary>The final notice to the person. Itself an outbox row.</summary>
    public const string Schlussnachricht = Praefix + "final";

    /// <summary>The origin's own tables, which fall last.</summary>
    public const string Identitaet = Praefix + "identity";

    /// <summary>
    /// The company withdrawal, deliberately <em>without</em> the prefix.
    /// </summary>
    /// <remarks>
    /// So it cannot be mistaken for part of the completeness proof. A silent
    /// jobs-service must not be able to hold a person's erasure open.
    /// </remarks>
    public const string Unternehmensrueckzug = "company.withdrawal";

    /// <summary>The kind for one foreign recipient.</summary>
    public static string Art(string empfaenger) => Praefix + empfaenger;
}
