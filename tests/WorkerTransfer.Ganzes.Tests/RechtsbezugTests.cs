using FluentAssertions;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;

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
        Rechtsbezuege.Alle.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        foreach (var bezug in Rechtsbezuege.Alle)
        {
            bezug.Leser.Should().NotBeNullOrWhiteSpace(
                $"{bezug.Fundstelle} muss sagen, was ein Mensch danach noch "
                + "entscheidet");

            // Nicht bloss „nicht leer": ein Punkt bestuende die erste
            // Behauptung und sagte nichts. Der kuerzeste echte Satz in der
            // Sammlung hat weit ueber vierzig Zeichen.
            bezug.Leser.Length.Should().BeGreaterThan(
                40, $"{bezug.Fundstelle}: ein Halbsatz ist keine Frage an einen Menschen");
        }
    }

    /// <summary>Und keines sagt, dass wir irgendetwas erfüllen.</summary>
    [Fact]
    public void Kein_Rechtsbezug_behauptet_Konformitaet()
    {
        foreach (var bezug in Rechtsbezuege.Alle)
        {
            foreach (var wort in Untersagt)
            {
                $"{bezug.Pflicht} {bezug.Leser}".Contains(
                        wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"{bezug.Fundstelle} trägt „{wort}“ — Belege, keine "
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

    /// <summary>Wo eine Frist gilt, steht sie da.</summary>
    /// <remarks>
    /// <strong>Ein Dokument, das eine Pflicht von 2027 so darstellt, als binde
    /// sie heute, lädt den Leser ein, zu früh Geld auszugeben.</strong> Die
    /// KI-VO läuft gestaffelt an: Art. 50 gilt seit dem 02.08.2026, Art. 12 und
    /// Art. 26 binden ab dem 02.12.2027. Anhang III ist die Einstufungsfrage
    /// selbst und hat kein eigenes Anlaufdatum.
    /// </remarks>
    [Fact]
    public void Jede_KI_Pflicht_mit_Anlaufdatum_traegt_es_auch()
    {
        Rechtsbezuege.Transparenz.Gilt.Should().Be("seit 02.08.2026");
        Rechtsbezuege.Aufzeichnung.Gilt.Should().Be("ab 02.12.2027");
        Rechtsbezuege.Betreiberpflichten.Gilt.Should().Be("ab 02.12.2027");

        Rechtsbezuege.AnhangIII.Gilt.Should().BeEmpty(
            "die Einstufungsfrage hat kein Anlaufdatum — sie ist die Frage, "
            + "von der die Fristen der anderen abhängen");
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
        Rechtsbezuege.AnhangIII.Leser.Should()
            .Contain("Mensch").And.Contain("juristischer Ausbildung");

        Rechtsbezuege.AnhangIII.Leser.Should().NotContain(
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
            .SelectMany(pruefung => pruefung.Bezuege)
            .Select(bezug => bezug.Fundstelle)
            .ToHashSet(StringComparer.Ordinal);

        belegt.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        Rechtsbezuege.Alle.Select(bezug => bezug.Fundstelle)
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
        var sammlung = Rechtsbezuege.Alle.ToHashSet();

        foreach (var pruefung in Pruefungen())
        {
            pruefung.Bezuege.Should().OnlyContain(bezug => sammlung.Contains(bezug),
                $"{pruefung.Id} nennt ein Zitat, das nicht in Rechtsbezuege.Alle steht");
        }
    }

    /// <summary>Die Bezüge reisen mit der Lesung, nicht mit dem Container.</summary>
    /// <remarks>
    /// Die Pflichtenseite liest sie aus der Lesung. Stünden sie nur an der
    /// Prüfung, müsste die Seite ein zweites Mal in den Container sehen — zwei
    /// Wege zu einer Aussage, und beim ersten Mal merkt es niemand.
    /// </remarks>
    [Fact]
    public async Task Ein_Befund_traegt_die_Bezuege_seiner_Pruefung()
    {
        var lesung = await new Nachweislauf(
            "probe",
            [new Aufzeichnungspruefung(gegenstandVorhanden: true, null)],
            TimeProvider.System).LeseAsync();

        lesung.Befunde.Single().Bezuege.Should()
            .Contain(Rechtsbezuege.Aufzeichnung)
            .And.Contain(Rechtsbezuege.AnhangIII);
    }

    /// <summary>Auch eine abgebrochene Prüfung nimmt ihre Bezüge mit.</summary>
    /// <remarks>
    /// Sonst verschwände ausgerechnet dort, wo etwas unbelegt bleibt, auch noch
    /// die Frage, für die es unbelegt bleibt.
    /// </remarks>
    [Fact]
    public async Task Auch_eine_werfende_Pruefung_traegt_ihre_Bezuege()
    {
        var lesung = await new Nachweislauf(
            "probe", [new WerferinMitBezug()], TimeProvider.System).LeseAsync();

        var befund = lesung.Befunde.Single();

        befund.Stand.Should().Be(Stand.Fehlt);
        befund.Bezuege.Should().Contain(Rechtsbezuege.Verzeichnis);
    }

    /// <summary>Jede Prüfung dieses Baumes, einmal gebaut.</summary>
    private static IReadOnlyList<IPruefung> Pruefungen() =>
    [
        new Zielpruefung(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
        new Nahtpruefung(typeof(object), [], "probe"),
        new Zahlpruefung([]),
        new Anbieterpruefung([]),
        new Aufzeichnungspruefung(false, null),
        new Widerrufspruefung<object>(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .BuildServiceProvider(),
            typeof(object)),
        Loeschpruefung.OhneZeilen("probe")
    ];

    /// <summary>Eine Prüfung, die wirft und Bezüge trägt.</summary>
    private sealed class WerferinMitBezug : IPruefung
    {
        public string Id => "wt.grenze.ziele";

        public Bereich Bereich => Bereich.Grenze;

        public IReadOnlyList<Rechtsbezug> Bezuege => [Rechtsbezuege.Verzeichnis];

        public Task<Befund> LaufenAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("im Test");
    }
}
