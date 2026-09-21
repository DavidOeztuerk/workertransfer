using Noelia.Abstractions.Compliance;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>The citations Noelia does not bring with it.</summary>
/// <remarks>
/// <para><strong>Noelia delivers ten, and they are not rebuilt here.</strong>
/// <c>RegulatoryReferences.GdprRecordsOfProcessing</c>,
/// <c>GdprThirdCountryTransfer</c>, <c>GdprProcessorContract</c>,
/// <c>AiActRecordKeeping</c>, <c>AiActDeployerDuties</c>,
/// <c>AiActTransparency</c> and the rest come out of the package. Two versions
/// of the same article would be worse than one: the reader can then no longer
/// say whether two findings are about <em>one</em> duty or about two.</para>
///
/// <para><strong>What stands here is missing there — looked up, not
/// assumed.</strong> Five articles, and each carries a question this product
/// raises and a to-do application does not: the automated decision, the
/// revocation, the erasure, the accountability and the classification question
/// from Annex III.</para>
///
/// <para><strong>And one is missing entirely.</strong> § 87 (1) no. 6 BetrVG
/// has no home in <c>RegulatoryRegime</c> — the enumeration knows <c>Gdpr</c>,
/// <c>AiAct</c>, <c>Nis2</c> and <c>Dora</c>, no national labour law. That is
/// not negligence on Noelia's part but its cut; for a product whose customer is
/// an employer it is nevertheless the gap that costs the most. It is filed as
/// <c>bugs/betrvg-hat-kein-regelwerk.md</c>, and until it is closed the
/// codetermination evidence carries the article in the text instead of as a
/// citation — <strong>visible, but not structured.</strong></para>
///
/// <para><c>Reader</c> is never empty, and that as a test. A citation that
/// settled its own article would be exactly the presumption the vocabulary
/// exists against.</para>
/// </remarks>
public static class AdditionalRegulatoryReferences
{
    /// <summary>Art. 22 GDPR — the automated decision in an individual case.</summary>
    /// <remarks>
    /// <strong>The article ADR-0022 builds around</strong>, and Noelia does not
    /// know it: a library does not know whether its consumer decides about
    /// people. This one appears to, and that is why the article stands here.
    /// </remarks>
    public static RegulatoryReference AutomatedDecision { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 22",
        "Eine Person hat das Recht, keiner ausschließlich auf automatisierter "
        + "Verarbeitung beruhenden Entscheidung unterworfen zu werden, die ihr "
        + "gegenüber rechtliche Wirkung entfaltet oder sie erheblich "
        + "beeinträchtigt.",
        "Ob die Auswahl, die auf dieser Plattform stattfindet, eine solche "
        + "Entscheidung ist — und wer sie trifft. Der Nachweis zeigt, dass keine "
        + "Zahl über einen Menschen entsteht; ob ein Mensch am Ende entscheidet, "
        + "zeigt kein Programm.");

    /// <summary>Art. 7 (3) GDPR — the revocation.</summary>
    public static RegulatoryReference Revocation { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 7 Abs. 3",
        "Eine Einwilligung ist jederzeit widerrufbar, und der Widerruf muss so "
        + "einfach sein wie die Erteilung.",
        "Ob der Weg zum Widerruf für die Person auffindbar ist. Der Nachweis "
        + "zeigt, dass zwischen der Frage und dem Ledger nichts steht — nicht, "
        + "ob jemand den Schalter findet.");

    /// <summary>Art. 17 GDPR — the erasure.</summary>
    public static RegulatoryReference Erasure { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 17",
        "Eine Person kann die Löschung ihrer Daten verlangen; der "
        + "Verantwortliche löscht unverzüglich.",
        "Ob „unverzüglich“ hier eingehalten ist und ob eine Ausnahme nach "
        + "Abs. 3 greift. Der Nachweis zeigt, ob die Kaskade wirken KANN — "
        + "nicht, wie lange sie im Einzelfall gebraucht hat.");

    /// <summary>Art. 5 (2) GDPR — the accountability duty.</summary>
    public static RegulatoryReference Accountability { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 5 Abs. 2",
        "Der Verantwortliche muss die Einhaltung der Grundsätze nachweisen "
        + "können.",
        "Ob die vorhandenen Belege für diesen Nachweis genügen. Eine Prüfspur "
        + "ist ein Beleg; ob sie der richtige ist, entscheidet, wer den Nachweis "
        + "führen muss.");

    /// <summary>Annex III no. 4 AI Act — employment.</summary>
    /// <remarks>
    /// <strong>The question this repository expressly does not
    /// answer.</strong> It is a judgement about the <em>use</em> and belongs to
    /// a human being with legal training. Noelia's <c>AiActDeployerDuties</c>
    /// stands beside it and asks after the deployer duties; which of them bite
    /// hangs on exactly this classification.
    /// </remarks>
    public static RegulatoryReference AnnexThree { get; } = new(
        RegulatoryRegime.AiAct,
        "Anhang III Nr. 4 Buchst. a",
        "Als hochriskant gelten KI-Systeme, die für die Einstellung oder Auswahl "
        + "natürlicher Personen bestimmt sind, insbesondere um gezielte "
        + "Stellenanzeigen zu schalten, Bewerbungen zu analysieren und zu "
        + "filtern und Bewerber zu bewerten.",
        "Ob der Einsatz in dieser Instanz darunter fällt. Diese Frage "
        + "beantwortet ein Mensch mit juristischer Ausbildung; der Nachweis legt "
        + "die Belege daneben und stuft nichts ein.");

    /// <summary>All five, in the order a human being reads them.</summary>
    public static IReadOnlyList<RegulatoryReference> Own { get; } =
    [
        AutomatedDecision,
        Revocation,
        Erasure,
        Accountability,
        AnnexThree
    ];

    /// <summary>
    /// § 87 (1) no. 6 BetrVG — as a sentence, because as a citation it cannot
    /// be one.
    /// </summary>
    /// <remarks>
    /// <strong>That is the gap, and it stands here visibly instead of being
    /// kept quiet.</strong> <c>RegulatoryRegime</c> knows no national labour
    /// law, so the works council codetermination cannot be expressed as a
    /// <see cref="RegulatoryReference"/>. Putting it under <c>Gdpr</c> would be
    /// a wrong citation in a document somebody passes around — worse than
    /// none.
    /// <para>
    /// It therefore travels along in the <em>text</em> of the findings
    /// concerned. Whoever assembles the evidence for a works council finds it
    /// there; a machine filtering by citations does not. Filed as
    /// <c>bugs/betrvg-hat-kein-regelwerk.md</c>.
    /// </para>
    /// </remarks>
    public const string WorksCouncilCodetermination =
        "§ 87 Abs. 1 Nr. 6 BetrVG: Der Betriebsrat bestimmt mit bei der "
        + "Einführung und Anwendung technischer Einrichtungen, die dazu bestimmt "
        + "sind, Verhalten oder Leistung der Arbeitnehmer zu überwachen. Was "
        + "daraus folgt — ob eine Betriebsvereinbarung nötig ist und was in ihr "
        + "steht —, entscheiden die Betriebsparteien.";
}
