using Noelia.Abstractions.Compliance;

namespace WorkerTransfer.ServiceDefaults.Pruefungen;

/// <summary>Die Zitate, die Noelia nicht mitbringt.</summary>
/// <remarks>
/// <para><strong>Noelia liefert zehn, und sie werden hier nicht nachgebaut.</strong>
/// <c>RegulatoryReferences.GdprRecordsOfProcessing</c>,
/// <c>GdprThirdCountryTransfer</c>, <c>GdprProcessorContract</c>,
/// <c>AiActRecordKeeping</c>, <c>AiActDeployerDuties</c>,
/// <c>AiActTransparency</c> und die übrigen kommen aus dem Paket. Zwei
/// Fassungen desselben Artikels wären schlimmer als eine: der Leser kann dann
/// nicht mehr sagen, ob zwei Befunde von <em>einer</em> Pflicht handeln oder von
/// zweien.</para>
///
/// <para><strong>Was hier steht, fehlt dort — nachgesehen, nicht angenommen.</strong>
/// Fünf Artikel, und jeder trägt eine Frage, die dieses Produkt stellt und eine
/// Todo-Anwendung nicht: die automatisierte Einzelentscheidung, der Widerruf,
/// die Löschung, die Rechenschaft und die Einstufungsfrage aus Anhang III.</para>
///
/// <para><strong>Und einer fehlt ganz.</strong> § 87 Abs. 1 Nr. 6 BetrVG hat in
/// <c>RegulatoryRegime</c> kein Zuhause — die Aufzählung kennt <c>Gdpr</c>,
/// <c>AiAct</c>, <c>Nis2</c> und <c>Dora</c>, kein nationales Arbeitsrecht. Das
/// ist keine Nachlässigkeit Noelias, sondern sein Zuschnitt; für ein Produkt,
/// dessen Kunde ein Arbeitgeber ist, ist es trotzdem die Lücke, die am meisten
/// kostet. Sie ist als <c>bugs/betrvg-hat-kein-regelwerk.md</c> gemeldet, und
/// bis sie geschlossen ist, trägt der Mitbestimmungsnachweis den Artikel im
/// Text statt als Zitat — <strong>sichtbar, aber nicht strukturiert.</strong></para>
///
/// <para><c>Reader</c> ist nie leer, und zwar als Test. Ein Zitat, das seinen
/// eigenen Artikel erledigte, wäre genau die Anmaßung, gegen die das Vokabular
/// existiert.</para>
/// </remarks>
public static class Rechtsbezuege
{
    /// <summary>Art. 22 DSGVO — die automatisierte Entscheidung im Einzelfall.</summary>
    /// <remarks>
    /// <strong>Der Artikel, um den ADR-0022 herumbaut</strong>, und Noelia kennt
    /// ihn nicht: eine Bibliothek weiß nicht, ob ihr Verbraucher über Menschen
    /// entscheidet. Dieser hier tut es dem Anschein nach, und deshalb steht der
    /// Artikel hier.
    /// </remarks>
    public static RegulatoryReference Einzelentscheidung { get; } = new(
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

    /// <summary>Art. 7 Abs. 3 DSGVO — der Widerruf.</summary>
    public static RegulatoryReference Widerruf { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 7 Abs. 3",
        "Eine Einwilligung ist jederzeit widerrufbar, und der Widerruf muss so "
        + "einfach sein wie die Erteilung.",
        "Ob der Weg zum Widerruf für die Person auffindbar ist. Der Nachweis "
        + "zeigt, dass zwischen der Frage und dem Ledger nichts steht — nicht, "
        + "ob jemand den Schalter findet.");

    /// <summary>Art. 17 DSGVO — die Löschung.</summary>
    public static RegulatoryReference Loeschung { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 17",
        "Eine Person kann die Löschung ihrer Daten verlangen; der "
        + "Verantwortliche löscht unverzüglich.",
        "Ob „unverzüglich“ hier eingehalten ist und ob eine Ausnahme nach "
        + "Abs. 3 greift. Der Nachweis zeigt, ob die Kaskade wirken KANN — "
        + "nicht, wie lange sie im Einzelfall gebraucht hat.");

    /// <summary>Art. 5 Abs. 2 DSGVO — die Rechenschaftspflicht.</summary>
    public static RegulatoryReference Rechenschaft { get; } = new(
        RegulatoryRegime.Gdpr,
        "Art. 5 Abs. 2",
        "Der Verantwortliche muss die Einhaltung der Grundsätze nachweisen "
        + "können.",
        "Ob die vorhandenen Belege für diesen Nachweis genügen. Eine Prüfspur "
        + "ist ein Beleg; ob sie der richtige ist, entscheidet, wer den Nachweis "
        + "führen muss.");

    /// <summary>Anhang III Nr. 4 KI-VO — Beschäftigung.</summary>
    /// <remarks>
    /// <strong>Die Frage, die dieses Repositorium ausdrücklich nicht
    /// beantwortet.</strong> Sie ist eine Beurteilung der <em>Nutzung</em> und
    /// gehört einem Menschen mit juristischer Ausbildung. Noelias
    /// <c>AiActDeployerDuties</c> steht daneben und fragt nach den
    /// Betreiberpflichten; welche davon greifen, hängt an genau dieser
    /// Einstufung.
    /// </remarks>
    public static RegulatoryReference AnhangIII { get; } = new(
        RegulatoryRegime.AiAct,
        "Anhang III Nr. 4 Buchst. a",
        "Als hochriskant gelten KI-Systeme, die für die Einstellung oder Auswahl "
        + "natürlicher Personen bestimmt sind, insbesondere um gezielte "
        + "Stellenanzeigen zu schalten, Bewerbungen zu analysieren und zu "
        + "filtern und Bewerber zu bewerten.",
        "Ob der Einsatz in dieser Instanz darunter fällt. Diese Frage "
        + "beantwortet ein Mensch mit juristischer Ausbildung; der Nachweis legt "
        + "die Belege daneben und stuft nichts ein.");

    /// <summary>Alle fünf, in der Reihenfolge, in der ein Mensch sie liest.</summary>
    public static IReadOnlyList<RegulatoryReference> Eigene { get; } =
    [
        Einzelentscheidung,
        Widerruf,
        Loeschung,
        Rechenschaft,
        AnhangIII
    ];

    /// <summary>
    /// § 87 Abs. 1 Nr. 6 BetrVG — als Satz, weil es ihn als Zitat nicht sein kann.
    /// </summary>
    /// <remarks>
    /// <strong>Das ist die Lücke, und sie steht hier sichtbar statt
    /// verschwiegen.</strong> <c>RegulatoryRegime</c> kennt kein nationales
    /// Arbeitsrecht, also lässt sich die Mitbestimmung nicht als
    /// <see cref="RegulatoryReference"/> ausdrücken. Sie auf <c>Gdpr</c> zu
    /// legen wäre eine falsche Fundstelle in einem Dokument, das jemand
    /// herumreicht — schlimmer als keine.
    /// <para>
    /// Sie reist deshalb im <em>Text</em> der betroffenen Befunde mit. Wer den
    /// Nachweis für einen Betriebsrat zusammenstellt, findet sie dort; eine
    /// Maschine, die nach Zitaten filtert, findet sie nicht. Gemeldet als
    /// <c>bugs/betrvg-hat-kein-regelwerk.md</c>.
    /// </para>
    /// </remarks>
    public const string Mitbestimmung =
        "§ 87 Abs. 1 Nr. 6 BetrVG: Der Betriebsrat bestimmt mit bei der "
        + "Einführung und Anwendung technischer Einrichtungen, die dazu bestimmt "
        + "sind, Verhalten oder Leistung der Arbeitnehmer zu überwachen. Was "
        + "daraus folgt — ob eine Betriebsvereinbarung nötig ist und was in ihr "
        + "steht —, entscheiden die Betriebsparteien.";
}
