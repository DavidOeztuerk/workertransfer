using Noelia.Core.Identity;

namespace WorkerTransfer.Advisor.Domain.Conversations;

/// <summary>How far a conversation is open.</summary>
/// <remarks>
/// <para><strong>No column, anywhere.</strong> The stage is derived from the
/// ledger on every read and never stored (ADR-0037 decision 2, ADR-0020). A
/// <c>stage</c> column would be a second truth about visibility — and it would
/// drift apart at the first revocation, exactly where somebody stayed visible
/// who no longer wanted to be.</para>
///
/// <para><strong><see cref="None"/> is a state, not a gap.</strong> For somebody
/// who has released nothing (any more), the conversation does not exist on the
/// other side: it appears in none of the company's lists and answers 404 when
/// asked directly — the same 404 as "does not exist".</para>
/// </remarks>
public enum Stage
{
    /// <summary>Nothing released. For the other side the conversation does not exist.</summary>
    None = 0,

    /// <summary>Profile, named skills, availability.</summary>
    Profile = 1,

    /// <summary>In addition evidence, the CV and the salary range.</summary>
    Documents = 2,

    /// <summary>In addition the civil name and contact.</summary>
    Person = 3
}

/// <summary>
/// The capabilities a stage consists of — and the only place they are written
/// down.
/// </summary>
/// <remarks>
/// <para><strong>They are the existing visibilities, not three new ones</strong>
/// (ADR-0037 decision 2). A second capability for the same thing ("profile
/// visible to company X") would be a second truth, and it drifts at the first
/// revocation. Exactly <em>one</em> is new: <see cref="CivilName"/>, because
/// there was none for "civil name and contact" before.</para>
///
/// <para><strong>And it carries no digit.</strong> <c>Capability</c> would
/// refuse <c>advisor.stage1</c> — the namespace admits no digits. That is not
/// an accident of the regex but the reason not to number here: a capability
/// says <em>what</em> becomes visible, never <em>how far</em> somebody has
/// come.</para>
///
/// <para><strong>Two sets per stage, and the difference carries the design.</strong>
/// <see cref="StandsOn"/> is the capability a stage <em>stands on</em>;
/// <see cref="Grants"/> is what a release <em>writes</em>. They come apart at
/// stages 1 and 2, because a stage promises more than one row: stage 1 also
/// promises availability (the market status), stage 2 also the certificates.
/// Whoever takes back one of those on its own loses exactly that — the stage
/// itself hangs on the row that makes it.</para>
/// </remarks>
public static class StageCapabilities
{
    /// <summary>"For all companies" — the switch on the profile page.</summary>
    /// <remarks>
    /// Read, never written: a stage release applies to <em>one</em> company.
    /// Whoever is already publicly visible therefore also stands at stage 1 —
    /// otherwise this service would claim a concealment that does not exist.
    /// </remarks>
    public const string ProfilePublic = "profile.visibility:public";

    /// <summary>The profile, for this one company.</summary>
    public static string Profile(TenantId company) => $"profile.visibility:tenant:{company.Value}";

    /// <summary>
    /// The market status, for this one company.
    /// </summary>
    /// <remarks>
    /// No <c>:public</c>, and there must not be one — releasing the market
    /// status always names a recipient (transfer-service says why). Stage 1
    /// names exactly one.
    /// </remarks>
    public static string Market(TenantId company) => $"market.visibility:tenant:{company.Value}";

    /// <summary>The CV, for this one company.</summary>
    public static string Resume(TenantId company) => $"resume.visibility:tenant:{company.Value}";

    /// <summary>The certificates, for this one company.</summary>
    public static string Documents(TenantId company) => $"documents.visibility:tenant:{company.Value}";

    /// <summary>
    /// Civil name and contact, for this one company.
    /// </summary>
    /// <remarks>
    /// The only new capability of this service. Like every other one in the
    /// system it is a <em>visibility</em>: "this company may see my civil
    /// name". That is why "delete <c>advisor.identity:tenant:…</c>" can never
    /// mean "delete the person" (ADR-0027 §1).
    /// </remarks>
    public static string CivilName(TenantId company) => $"advisor.identity:tenant:{company.Value}";

    /// <summary>What a stage stands on — one capability per stage.</summary>
    /// <remarks>
    /// Stage 1 stands on <em>two</em> possibilities, because "for everyone" is
    /// the wider one: whoever is publicly visible is visible to this company too.
    /// </remarks>
    public static IReadOnlyList<string> StandsOn(Stage stage, TenantId company) => stage switch
    {
        Stage.Profile => [Profile(company), ProfilePublic],
        Stage.Documents => [Resume(company)],
        Stage.Person => [CivilName(company)],
        _ => []
    };

    /// <summary>What a release of this stage writes.</summary>
    /// <remarks>
    /// <para>Stage 1 also writes the market status, because it promises
    /// "availability" and availability <em>is</em> the market status. A stage
    /// that promises three things and silently delivers two would be exactly
    /// the "locked" hint this design does not give — only the other way
    /// round.</para>
    ///
    /// <para><strong>It does not grant approachability with that.</strong>
    /// <c>unavailable</c> still means no, release or not: the release allows
    /// seeing, not disturbing. That rule lives in transfer-service and is not
    /// rebuilt here.</para>
    ///
    /// <para><strong><c>github.visibility:public</c> is deliberately not here.</strong>
    /// It is platform-wide and has no per-company form; writing it along with a
    /// stage release would turn a release to <em>one</em> company into one to
    /// all. Evidence travels along with stage 2 when the person has made it
    /// public anyway — it is never granted here.</para>
    /// </remarks>
    public static IReadOnlyList<string> Grants(Stage stage, TenantId company) => stage switch
    {
        Stage.Profile => [Profile(company), Market(company)],
        Stage.Documents => [Resume(company), Documents(company)],
        Stage.Person => [CivilName(company)],
        _ => []
    };

    /// <summary>The three stages, from the bottom up.</summary>
    public static readonly IReadOnlyList<Stage> All =
    [
        Stage.Profile, Stage.Documents, Stage.Person
    ];

    /// <summary>The stage as it stands on the wire.</summary>
    public static int ToWire(Stage stage) => (int)stage;

    /// <summary>The stage for a wire value, or <c>null</c>.</summary>
    public static Stage? FromWire(int? value) => value switch
    {
        1 => Stage.Profile,
        2 => Stage.Documents,
        3 => Stage.Person,
        _ => null
    };
}
