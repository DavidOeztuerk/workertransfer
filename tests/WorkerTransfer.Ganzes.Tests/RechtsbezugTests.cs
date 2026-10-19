using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults.Pruefungen;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>Belege, keine Konformität — und zwar als Test.</summary>
/// <remarks>
/// <para><strong>Die Wortliste ist ein echtes Risiko, kein theoretisches.</strong>
/// Nach Art. 42/43 DSGVO darf nur eine Aufsichtsbehörde oder eine nach
/// EN ISO/IEC 17065 akkreditierte Stelle zertifizieren; BSI C5 attestiert nur
/// ein zugelassener Prüfer, SecNumCloud qualifiziert nur die ANSSI. Wir sind
/// nichts davon. „Zertifiziert“ ohne Akkreditierung ist in der EU eine
/// irreführende Geschäftspraxis — RL 2005/29/EG gegenüber Verbrauchern,
/// RL 2006/114/EG zwischen Unternehmen.</para>
///
/// <para><strong>Deshalb ein Test und keine Durchsicht.</strong> Ein Satz, den
/// jemand in zwei Monaten in eine Zusammenfassung schreibt, wird nicht
/// durchgesehen — er wird geschrieben und veröffentlicht. Vanta, Drata und
/// Secureframe schreiben aus demselben Grund überall, SOC 2 sei eine
/// <em>attestation</em> und keine <em>certification</em>; ihnen zu folgen ist
/// der sichere Weg, nicht der ängstliche.</para>
/// </remarks>
public class RechtsbezugTests
{
    /// <summary>Worte, die eine Zusage machen, die niemand hier geben darf.</summary>
    /// <remarks>
    /// <c>attest</c> steht mit dabei: ein „Attest“ ist im Deutschen genauso
    /// eine Bescheinigung wie ein Zertifikat, und der englische Ausweg der
    /// amerikanischen Anbieter trägt hier gerade nicht.
    /// </remarks>
    public static IReadOnlyList<string> Untersagt { get; } =
    [
        "zertifi", "zertifikat", "konform", "compliant", "certif",
        "bescheinig", "attest", "auditiert", "geprüft durch", "erfüllt art",
        "dsgvo-konform"
    ];

    /// <summary>Kein Zitat erledigt seinen eigenen Artikel.</summary>
    /// <remarks>
    /// <strong>Der Test, den der Auftrag namentlich verlangt.</strong> Ein
    /// <c>Leser</c>, der leer bliebe, wäre die Behauptung, nach diesem Befund
    /// sei nichts mehr zu entscheiden — genau die Anmaßung, gegen die das
    /// ganze Vokabular existiert.
    /// </remarks>
    [Fact]
    public void Kein_Rechtsbezug_hat_einen_leeren_Leser()
    {
        Rechtsbezuege.Eigene.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        foreach (var bezug in Rechtsbezuege.Eigene)
        {
            bezug.Reader.Should().NotBeNullOrWhiteSpace(
                $"{bezug.Citation} muss sagen, was ein Mensch danach noch "
                + "entscheidet");

            // Nicht bloss „nicht leer": ein Punkt bestuende die erste
            // Behauptung und sagte nichts. Der kuerzeste echte Satz in der
            // Sammlung hat weit ueber vierzig Zeichen.
            bezug.Reader.Length.Should().BeGreaterThan(
                40, $"{bezug.Citation}: ein Halbsatz ist keine Frage an einen Menschen");
        }
    }

    /// <summary>Und keines sagt, dass wir irgendetwas erfüllen.</summary>
    [Fact]
    public void Kein_Rechtsbezug_behauptet_Konformitaet()
    {
        foreach (var bezug in Rechtsbezuege.Eigene)
        {
            foreach (var wort in Untersagt)
            {
                $"{bezug.Obligation} {bezug.Reader}".Contains(
                        wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"{bezug.Citation} trägt „{wort}“ — Belege, keine "
                        + "Konformität");
            }
        }
    }

    /// <summary>Die Erkennung selbst stimmt.</summary>
    /// <remarks>
    /// Sonst wäre der Test oben grün, weil er nichts sucht — dieselbe Frage wie
    /// bei jeder Verbotsliste.
    /// </remarks>
    [Theory]
    [InlineData("Eine Tatsache, nach der Art. 30 fragt.", false)]
    [InlineData("Beleg für Art. 30 Abs. 1.", false)]
    [InlineData("Dieses System ist DSGVO-konform.", true)]
    [InlineData("Hiermit zertifiziert.", true)]
    [InlineData("Wir bescheinigen die Einhaltung.", true)]
    [InlineData("Geprüft durch eine unabhängige Stelle.", true)]
    public void Die_Wortliste_trifft_genau_das_Gemeinte(string satz, bool erwartet) =>
        Untersagt.Any(wort => satz.Contains(wort, StringComparison.OrdinalIgnoreCase))
            .Should().Be(erwartet);

    /// <summary>Unsere fünf sind genau die, die Noelia nicht hat.</summary>
    /// <remarks>
    /// <strong>Zwei Fassungen desselben Artikels wären schlimmer als eine</strong>
    /// — der Leser könnte dann nicht mehr sagen, ob zwei Befunde von einer
    /// Pflicht handeln oder von zweien. Dieser Test hält fest, dass wir nichts
    /// nachbauen, was aus dem Paket kommt.
    /// </remarks>
    [Fact]
    public void Kein_eigenes_Zitat_gibt_es_bei_Noelia_schon()
    {
        var vonNoelia = new[]
        {
            RegulatoryReferences.GdprRecordsOfProcessing,
            RegulatoryReferences.GdprThirdCountryTransfer,
            RegulatoryReferences.GdprProcessorContract,
            RegulatoryReferences.AiActRecordKeeping,
            RegulatoryReferences.AiActDeployerDuties,
            RegulatoryReferences.AiActTransparency
        }.Select(bezug => bezug.Citation).ToHashSet(StringComparer.Ordinal);

        Rechtsbezuege.Eigene.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        Rechtsbezuege.Eigene.Select(bezug => bezug.Citation)
            .Should().NotIntersectWith(vonNoelia);
    }

    /// <summary>
    /// § 87 Abs. 1 Nr. 6 BetrVG ist kein Zitat, weil er keines sein kann.
    /// </summary>
    /// <remarks>
    /// <strong>Die Lücke steht sichtbar statt verschwiegen.</strong>
    /// <c>RegulatoryRegime</c> kennt <c>Gdpr</c>, <c>AiAct</c>, <c>Nis2</c> und
    /// <c>Dora</c> — kein nationales Arbeitsrecht. Gemeldet als
    /// <c>bugs/betrvg-hat-kein-regelwerk.md</c>; dieser Test wird grün bleiben,
    /// bis es ein Regelwerk dafür gibt, und dann fällt er und verlangt, dass die
    /// Konstante ein Zitat wird.
    /// </remarks>
    [Fact]
    public void Die_Mitbestimmung_ist_ein_Satz_und_kein_falsches_Zitat()
    {
        Rechtsbezuege.Mitbestimmung.Should().Contain("§ 87 Abs. 1 Nr. 6 BetrVG");

        Enum.GetNames<RegulatoryRegime>().Should().NotContain(
            name => name.Contains("Betr", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("National", StringComparison.OrdinalIgnoreCase),
            "sobald es ein Regelwerk für nationales Arbeitsrecht gibt, gehört "
            + "die Mitbestimmung als RegulatoryReference dahin — und dieser "
            + "Test fällt und sagt es");
    }

    /// <summary>Anhang III stuft nicht ein, und sagt das.</summary>
    /// <remarks>
    /// <strong>Schreibe nie, dass WorkerTransfer nicht hochriskant ist.</strong>
    /// Schreibe, welche Belege vorliegen und wer die Frage beantwortet. Dieser
    /// Test hält den zweiten Teil fest.
    /// </remarks>
    [Fact]
    public void Anhang_III_gibt_die_Frage_an_einen_Menschen_weiter()
    {
        Rechtsbezuege.AnhangIII.Reader.Should()
            .Contain("Mensch").And.Contain("juristischer Ausbildung");

        Rechtsbezuege.AnhangIII.Reader.Should().NotContain(
            "nicht hochriskant",
            "die Einstufung gehört nicht uns — der Nachweis legt die Belege "
            + "daneben und stuft nichts ein");
    }

    /// <summary>Jedes Zitat wird von mindestens einer Prüfung belegt.</summary>
    /// <remarks>
    /// <strong>Ein Zitat ohne Befund ist eine Rechtsauskunft.</strong> Die
    /// Sammlung ist dazu da, dass zwei Prüfungen denselben Artikel gleich
    /// nennen — nicht dazu, eine Liste von Pflichten zu führen, zu der dieses
    /// System nichts sagen kann. Wer einen Artikel aufnimmt, ohne dass ihn eine
    /// Prüfung belegt, baut genau das.
    /// </remarks>
    [Fact]
    public void Kein_Zitat_steht_ohne_Pruefung_da()
    {
        var belegt = Pruefungen()
            .SelectMany(pruefung => pruefung.References)
            .Select(bezug => bezug.Citation)
            .ToHashSet(StringComparer.Ordinal);

        belegt.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        Rechtsbezuege.Eigene.Select(bezug => bezug.Citation)
            .Should().OnlyContain(fundstelle => belegt.Contains(fundstelle));
    }

    /// <summary>Und jede Prüfung nennt nur Zitate aus der Sammlung.</summary>
    /// <remarks>
    /// Die Gegenrichtung, und die wichtigere: ein Zitat, das eine Prüfung sich
    /// selbst schreibt, ist die zweite Fassung desselben Artikels — und dann
    /// kann der Leser nicht mehr sagen, ob zwei Befunde von einer Pflicht
    /// handeln oder von zweien.
    /// </remarks>
    [Fact]
    public void Keine_Pruefung_erfindet_ein_eigenes_Zitat()
    {
        // Erlaubt ist BEIDES: was Noelia mitbringt und was wir ergaenzen. Der
        // Punkt dieses Tests ist ein anderer — dass niemand einen Artikel
        // NEBEN einer der beiden Sammlungen erfindet, denn dann stuenden zwei
        // Fassungen desselben Zitats im selben Dokument.
        var sammlung = Rechtsbezuege.Eigene
            .Concat(
            [
                RegulatoryReferences.GdprRecordsOfProcessing,
                RegulatoryReferences.GdprThirdCountryTransfer,
                RegulatoryReferences.GdprProcessorContract,
                RegulatoryReferences.GdprIntegrityOfProcessing,
                RegulatoryReferences.AiActRecordKeeping,
                RegulatoryReferences.AiActDeployerDuties,
                RegulatoryReferences.AiActTransparency,
                RegulatoryReferences.Nis2SupplyChain,
                RegulatoryReferences.DoraAssetIdentification,
                RegulatoryReferences.DoraThirdPartyRegister
            ])
            .ToHashSet();

        foreach (var pruefung in Pruefungen())
        {
            pruefung.References.Should().OnlyContain(bezug => sammlung.Contains(bezug),
                $"{pruefung.Id} nennt ein Zitat, das nicht in Rechtsbezuege.Eigene steht");
        }
    }

    /// <summary>Jede Prüfung trägt ihre Zitate selbst.</summary>
    /// <remarks>
    /// <strong>Das ersetzt zwei Tests, die unseren eigenen Läufer prüften.</strong>
    /// Bis ADR-0045 hängte <c>Nachweislauf</c> die Bezüge einer Prüfung an den
    /// Befund, wenn sie es nicht selbst tat — und zwei Tests hielten das fest.
    /// Noelias <c>SecurityCheckResult</c> trägt <c>References</c> direkt, also
    /// hängt sie jede Prüfung selbst an, und geprüft wird genau das.
    /// </remarks>
    [Fact]
    public async Task Jeder_Befund_traegt_die_Zitate_seiner_Pruefung()
    {
        foreach (var pruefung in Pruefungen())
        {
            var befund = await pruefung.RunAsync();

            befund.References.Should().BeEquivalentTo(
                pruefung.References,
                $"{pruefung.Id} muss seine Zitate an den Befund hängen — sonst "
                + "steht die Pflichtenseite ohne sie da");
        }
    }

    /// <summary>Jede Prüfung dieses Baumes, die Zitate trägt.</summary>
    /// <remarks>
    /// Ohne Container gebaut: <c>Widerrufspruefung</c> und <c>Ledgerpruefung</c>
    /// brauchen einen, und ihre Zitate sind dieselben wie die der anderen.
    /// </remarks>
    private static IReadOnlyList<ISecurityCheck> Pruefungen() =>
    [
        new Nahtpruefung(typeof(object), [], "probe"),
        new Zahlpruefung([]),
        new Anbieterpruefung([]),
        Loeschpruefung.OhneZeilen("probe"),
        // Ohne registriertes Tor meldet sie NotApplicable — ihre Zitate traegt
        // sie trotzdem, und genau die fehlten dieser Liste.
        new Widerrufspruefung<object>(
            new ServiceCollection().BuildServiceProvider(), typeof(object))
    ];
}
