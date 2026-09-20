namespace WorkerTransfer.Nachweis;

/// <summary>Die Zitate, an einer Stelle.</summary>
/// <remarks>
/// <para><strong>Warum sie hier stehen und nicht bei den Prüfungen.</strong>
/// Derselbe Artikel wird von mehreren Prüfungen belegt — Art. 30 Abs. 1 DSGVO
/// vom Anbieterverzeichnis <em>und</em> von den Zielen der Egress-Grenze. Zwei
/// Fassungen desselben Zitats sind schlimmer als keines: der Leser kann dann
/// nicht mehr sagen, ob zwei Befunde von <em>einer</em> Pflicht handeln oder
/// von zweien, und die Pflichtenseite zeigte denselben Artikel zweimal mit
/// zwei Sätzen darunter.</para>
///
/// <para><strong>Kein Eintrag sagt, dass wir etwas erfüllen.</strong>
/// <see cref="Rechtsbezug.Pflicht"/> sagt, wonach der Artikel fragt;
/// <see cref="Rechtsbezug.Leser"/> sagt, was danach ein Mensch entscheidet, und
/// ist nie leer. Ein Programm kann zeigen, dass etwas vorhanden ist, wann es
/// entstanden ist und dass es unverändert ist — Angemessenheit kann es nicht
/// zeigen.</para>
///
/// <para><strong>Souveränität hat hier keinen Eintrag.</strong> Keine
/// Verordnung verlangt sie. Wo der Nachweis davon spricht, misst er eine
/// Entscheidung des Betreibers, nicht die Übereinstimmung mit einer Regel — und
/// ein Eintrag machte aus einer Haltung eine Pflicht, die niemand geschrieben
/// hat.</para>
/// </remarks>
public static class Rechtsbezuege
{
    // ------------------------------------------------------------------ DSGVO

    /// <summary>Art. 30 Abs. 1 DSGVO — das Verzeichnis der Verarbeitungstätigkeiten.</summary>
    /// <remarks>
    /// Der Artikel, für den das Anbieterverzeichnis gebaut ist: ein KI-Anbieter,
    /// an den ein Prompt geht, <em>ist</em> ein Empfänger im Sinne von Buchst. d.
    /// </remarks>
    public static Rechtsbezug Verzeichnis { get; } = new(
        Regelwerk.Dsgvo,
        "Art. 30 Abs. 1 Buchst. d, e",
        "Das Verzeichnis der Verarbeitungstätigkeiten nennt die Kategorien von "
        + "Empfängern und, wo es sie gibt, die Übermittlungen in ein Drittland.",
        "Ob dieser Empfänger im Verzeichnis des Verantwortlichen steht, in "
        + "welcher Kategorie, und für welche Verarbeitungstätigkeit. Der "
        + "Nachweis nennt, wer angesprochen wird — nicht, wofür.");

    /// <summary>Kap. V DSGVO — die Übermittlung in ein Drittland.</summary>
    public static Rechtsbezug Drittland { get; } = new(
        Regelwerk.Dsgvo,
        "Kap. V (Art. 44–49)",
        "Eine Übermittlung personenbezogener Daten in ein Drittland braucht "
        + "eine Grundlage: einen Angemessenheitsbeschluss, geeignete Garantien "
        + "oder eine Ausnahme.",
        "In welchem Land dieses Ziel verarbeitet, welche Garantie es deckt und "
        + "ob dafür eine Übermittlungs-Folgenabschätzung vorliegt. Ein Hostname "
        + "sagt keines davon — und ihn aufzulösen wäre eine Schätzung im Gewand "
        + "einer Messung.");

    /// <summary>Art. 28 Abs. 3 DSGVO — die Auftragsverarbeitung.</summary>
    public static Rechtsbezug Auftragsverarbeitung { get; } = new(
        Regelwerk.Dsgvo,
        "Art. 28 Abs. 3",
        "Wer im Auftrag verarbeitet, tut das auf Grundlage eines Vertrags, der "
        + "Gegenstand, Dauer, Art und Zweck festlegt.",
        "Ob mit diesem Anbieter ein Auftragsverarbeitungsvertrag besteht und ob "
        + "er trägt. Das steht in einem Aktenschrank, und kein Programm sieht "
        + "hinein.");

    /// <summary>Art. 22 DSGVO — die automatisierte Entscheidung im Einzelfall.</summary>
    /// <remarks>
    /// <strong>Der Artikel, um den ADR-0022 herumbaut.</strong> Die Regel dort
    /// lautet: Anforderung rein, Belege raus — nie Mensch rein, Zahl raus. Ob
    /// das trägt, entscheidet nicht dieses Repositorium.
    /// </remarks>
    public static Rechtsbezug Einzelentscheidung { get; } = new(
        Regelwerk.Dsgvo,
        "Art. 22",
        "Eine Person hat das Recht, keiner ausschließlich auf automatisierter "
        + "Verarbeitung beruhenden Entscheidung unterworfen zu werden, die ihr "
        + "gegenüber rechtliche Wirkung entfaltet oder sie erheblich "
        + "beeinträchtigt.",
        "Ob die Auswahl, die auf dieser Plattform stattfindet, eine solche "
        + "Entscheidung ist — und wer sie trifft. Der Nachweis zeigt, dass "
        + "keine Zahl über einen Menschen entsteht; ob ein Mensch am Ende "
        + "entscheidet, zeigt kein Programm.");

    /// <summary>Art. 7 Abs. 3 DSGVO — der Widerruf.</summary>
    public static Rechtsbezug Widerruf { get; } = new(
        Regelwerk.Dsgvo,
        "Art. 7 Abs. 3",
        "Eine Einwilligung ist jederzeit widerrufbar, und der Widerruf muss so "
        + "einfach sein wie die Erteilung.",
        "Ob der Weg zum Widerruf für die Person auffindbar ist. Der Nachweis "
        + "zeigt, dass zwischen der Frage und dem Ledger nichts steht — nicht, "
        + "ob jemand den Schalter findet.");

    /// <summary>Art. 17 DSGVO — die Löschung.</summary>
    public static Rechtsbezug Loeschung { get; } = new(
        Regelwerk.Dsgvo,
        "Art. 17",
        "Eine Person kann die Löschung ihrer Daten verlangen; der "
        + "Verantwortliche löscht unverzüglich.",
        "Ob „unverzüglich“ hier eingehalten ist und ob eine Ausnahme nach "
        + "Abs. 3 greift. Der Nachweis zeigt, ob die Kaskade wirken KANN — "
        + "nicht, wie lange sie im Einzelfall gebraucht hat.");

    /// <summary>Art. 5 Abs. 2 DSGVO — die Rechenschaftspflicht.</summary>
    public static Rechtsbezug Rechenschaft { get; } = new(
        Regelwerk.Dsgvo,
        "Art. 5 Abs. 2",
        "Der Verantwortliche muss die Einhaltung der Grundsätze nachweisen "
        + "können.",
        "Ob die vorhandenen Belege für diesen Nachweis genügen. Eine Prüfspur "
        + "ist ein Beleg; ob sie der richtige ist, entscheidet, wer den "
        + "Nachweis führen muss.");

    // ------------------------------------------------------------------ KI-VO

    /// <summary>Art. 50 KI-VO — die Transparenz gegenüber der Person.</summary>
    /// <remarks>
    /// <strong>Seit dem 02.08.2026 in Kraft</strong>, und damit der einzige
    /// KI-VO-Eintrag hier, der heute bindet.
    /// </remarks>
    public static Rechtsbezug Transparenz { get; } = new(
        Regelwerk.KiVo,
        "Art. 50",
        "Wer mit einem KI-System zu tun hat, muss das erfahren; erzeugte "
        + "Inhalte sind als solche erkennbar zu machen.",
        "Ob die Hinweise in der Oberfläche an der richtigen Stelle stehen und "
        + "genügen. Der Nachweis zeigt, welche Modelle angesprochen werden — "
        + "nicht, was ein Mensch davor gelesen hat.",
        Gilt: "seit 02.08.2026");

    /// <summary>Art. 12 und Art. 26 Abs. 6 KI-VO — die automatische Aufzeichnung.</summary>
    /// <remarks>
    /// <strong>Bindet ab dem 02.12.2027</strong>, und nur, soweit der Einsatz
    /// unter Anhang III fällt. Ein Dokument, das diese Pflicht so darstellt, als
    /// binde sie heute, lädt den Leser ein, zu früh Geld auszugeben.
    /// </remarks>
    public static Rechtsbezug Aufzeichnung { get; } = new(
        Regelwerk.KiVo,
        "Art. 12, Art. 26 Abs. 6",
        "Ein Hochrisikosystem zeichnet seine Tätigkeit automatisch auf; der "
        + "Betreiber bewahrt die Aufzeichnungen mindestens sechs Monate auf.",
        "Ob diese Pflicht diesen Einsatz überhaupt erfasst — das hängt an der "
        + "Einstufung nach Anhang III, und die beantwortet ein Mensch mit "
        + "juristischer Ausbildung.",
        Gilt: "ab 02.12.2027");

    /// <summary>Art. 26 KI-VO — die Pflichten des Betreibers.</summary>
    public static Rechtsbezug Betreiberpflichten { get; } = new(
        Regelwerk.KiVo,
        "Art. 26",
        "Der Betreiber eines Hochrisikosystems benutzt es nach der "
        + "Betriebsanleitung, sorgt für menschliche Aufsicht und unterrichtet "
        + "die Beschäftigten, bevor er es am Arbeitsplatz einsetzt.",
        "Ob eine menschliche Aufsicht eingerichtet ist und ob sie wirksam ist. "
        + "Wirksamkeit ist genau das, was ein Programm nicht zeigen kann.",
        Gilt: "ab 02.12.2027");

    /// <summary>Anhang III Nr. 4 KI-VO — Beschäftigung.</summary>
    /// <remarks>
    /// <strong>Die Frage, die dieses Repositorium ausdrücklich nicht
    /// beantwortet.</strong> Sie ist eine Beurteilung der <em>Nutzung</em> und
    /// gehört einem Menschen mit juristischer Ausbildung. Was hier entsteht,
    /// ist das, was dieser Mensch braucht, um sie zu beantworten.
    /// </remarks>
    public static Rechtsbezug AnhangIII { get; } = new(
        Regelwerk.KiVo,
        "Anhang III Nr. 4 Buchst. a",
        "Als hochriskant gelten KI-Systeme, die für die Einstellung oder "
        + "Auswahl natürlicher Personen bestimmt sind, insbesondere um gezielte "
        + "Stellenanzeigen zu schalten, Bewerbungen zu analysieren und zu "
        + "filtern und Bewerber zu bewerten.",
        "Ob der Einsatz in dieser Instanz darunter fällt. Diese Frage "
        + "beantwortet ein Mensch mit juristischer Ausbildung; der Nachweis "
        + "legt die Belege daneben und stuft nichts ein.");

    // ----------------------------------------------------------------- BetrVG

    /// <summary>§ 87 Abs. 1 Nr. 6 BetrVG — die Mitbestimmung.</summary>
    /// <remarks>
    /// Der Grund, aus dem diese Dokumente überhaupt für jemanden außerhalb des
    /// Hauses lesbar sein müssen: bevor bei einem Kunden ein System eingeführt
    /// wird, das geeignet ist, Verhalten oder Leistung zu überwachen, redet der
    /// Betriebsrat mit.
    /// </remarks>
    public static Rechtsbezug Mitbestimmung { get; } = new(
        Regelwerk.BetrVG,
        "§ 87 Abs. 1 Nr. 6",
        "Der Betriebsrat bestimmt mit bei der Einführung und Anwendung "
        + "technischer Einrichtungen, die dazu bestimmt sind, Verhalten oder "
        + "Leistung der Arbeitnehmer zu überwachen.",
        "Ob eine Betriebsvereinbarung nötig ist und was in ihr steht. Der "
        + "Nachweis sagt, was dieses System über Beschäftigte erfassen kann und "
        + "was nachweislich nicht — die Bewertung dessen gehört den "
        + "Betriebsparteien.");

    /// <summary>Alle Zitate, in der Reihenfolge, in der ein Mensch sie liest.</summary>
    /// <remarks>
    /// Erst DSGVO, dann KI-VO, dann BetrVG — drei Regelwerke, drei Leser. Die
    /// Reihenfolge ist fest, damit die Pflichtenseite zweier Lesungen
    /// desselben Stands dieselbe ist und eine Signatur darüber nachgerechnet
    /// werden kann.
    /// </remarks>
    public static IReadOnlyList<Rechtsbezug> Alle { get; } =
    [
        Verzeichnis,
        Drittland,
        Auftragsverarbeitung,
        Einzelentscheidung,
        Widerruf,
        Loeschung,
        Rechenschaft,
        Transparenz,
        Aufzeichnung,
        Betreiberpflichten,
        AnhangIII,
        Mitbestimmung
    ];
}
