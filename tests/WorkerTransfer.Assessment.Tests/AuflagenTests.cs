using System.Reflection;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Application.Vorgaenge;
using WorkerTransfer.Assessment.Contracts;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.Assessment.Infrastructure.Persistence;

namespace WorkerTransfer.Assessment.Tests;

/// <summary>
/// Die drei Sätze, um derentwillen dieser Dienst gebaut werden durfte — jeder
/// als Test.
/// </summary>
/// <remarks>
/// <para>Sie stehen in <em>einer</em> Datei, und das ist Absicht: wer einen
/// davon rot macht, hat nicht einen Test gebrochen, sondern die Begründung des
/// ganzen Dienstes (ADR-0042).</para>
///
/// <para>Geprüft wird, wo möglich, an <em>Typen</em> und am <em>EF-Modell</em>
/// statt am Verhalten: eine Verhaltensprobe beantwortet „tut es das heute
/// nicht?", eine Typprobe verlangt, dass wer es ändert, die Menge hier
/// hinschreibt. Dieselbe Bewegung, aber eine, die auffällt.</para>
/// </remarks>
public class AuflagenTests
{
    private static readonly Guid Firma = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Fremde = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Anna = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Jetzt =
        new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------
    // AUFLAGE 1 — DIE BEWERTUNG GEHÖRT DEM VORGANG, NICHT DEM MENSCHEN
    // ---------------------------------------------------------------------

    /// <summary>Worte, aus denen eine Note über einen Menschen würde.</summary>
    /// <remarks>
    /// <para>Der Zwilling zu <c>Adr0022Tests</c> und zu den Auflagen des
    /// scout-service. Verboten ist die <em>Bewertungszahl</em>, nicht die Zahl:
    /// <c>hours</c> steht ausdrücklich nicht auf dieser Liste, weil sie von der
    /// <em>Aufgabe</em> handelt und nicht vom Menschen. Genau das ist die
    /// Trennlinie von ADR-0022 — Anforderung rein, Belege raus; niemals Mensch
    /// rein, Zahl raus.</para>
    ///
    /// <para><c>stufe</c> und <c>level</c> stehen mit darauf: eine Stufe wäre
    /// eine Rangfolge mit anderem Namen.</para>
    /// </remarks>
    private static readonly string[] Verboten =
    [
        "score", "rank", "grade", "rating", "punkte", "note", "sterne", "stars",
        "percent", "prozent", "weight", "gewicht", "fit", "passung", "level", "stufe"
    ];

    /// <summary>Keine Spalte des EF-Modells trägt ein solches Wort.</summary>
    /// <remarks>
    /// Am <em>Modell</em> und nicht am Quelltext: eine Spalte kann aus einer
    /// Basisklasse oder einer Konvention kommen, und ein regulärer Ausdruck
    /// übersähe sie. Was hier gelesen wird, ist genau das, was in der Datenbank
    /// entsteht.
    /// </remarks>
    [Fact]
    public void Keine_Spalte_traegt_eine_Note()
    {
        var spalten = Spalten();

        spalten.Should().NotBeEmpty("sonst prüft dieser Test nichts");
        spalten.Should().Contain("hours", "die erlaubte Zahl muss wirklich dastehen");

        foreach (var spalte in spalten)
        {
            foreach (var wort in Verboten)
            {
                spalte.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{spalte}' trägt '{wort}' — eine Bewertung hat Text und keine Zahl "
                        + "(ADR-0022, ADR-0042 §1)");
            }
        }
    }

    /// <summary>Und kein Name in Domäne und Vertrag.</summary>
    /// <remarks>
    /// Zwei Baugruppen, weil sie die zwei Stellen sind, an denen eine Zahl
    /// entstehen könnte: das Aggregat speichert sie, der Vertrag gäbe sie
    /// heraus. Die Anwendungsschicht rechnet nicht.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Baugruppen))]
    public void Kein_Name_traegt_eine_Note(string welche, Assembly baugruppe)
    {
        var namen = Namen(baugruppe);

        namen.Should().NotBeEmpty($"{welche} muss lesbar sein, sonst prüft das hier nichts");

        foreach (var name in namen)
        {
            foreach (var wort in Verboten)
            {
                name.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{name}' in {welche} trägt '{wort}'");
            }
        }
    }

    /// <summary>Die zwei Baugruppen, die nichts rechnen dürfen.</summary>
    public static TheoryData<string, Assembly> Baugruppen => new()
    {
        { "Domain", typeof(Vorgang).Assembly },
        { "Contracts", typeof(VorgangV1).Assembly }
    };

    /// <summary>Die Erkennung selbst stimmt — sonst wäre der Test grün, weil er nichts findet.</summary>
    [Theory]
    [InlineData("hours", false)]
    [InlineData("evaluation_text", false)]
    [InlineData("submission_url", false)]
    [InlineData("due_at", false)]
    [InlineData("score", true)]
    [InlineData("gesamtnote", true)]
    [InlineData("fit_percent", true)]
    [InlineData("sterne", true)]
    public void Die_Erkennung_trennt_richtig(string name, bool erwartet) =>
        Verboten.Any(wort => name.Contains(wort, StringComparison.OrdinalIgnoreCase))
            .Should().Be(erwartet);

    /// <summary>Eine Bewertung trägt genau diese Felder — Text, Ausgang, Zeitpunkt.</summary>
    /// <remarks>
    /// Die geschlossene Feldmenge, nicht eine Verbotsliste: wer ein Feld
    /// ergänzt, schreibt es hier hin. <strong>Und es ist genau EIN Textfeld</strong>
    /// — kein internes daneben, keine Notiz, kein „nur für uns". Wo es zwei
    /// gäbe, stünde im zweiten die Wahrheit, und die Person läse das andere
    /// (ADR-0042 §2).
    /// </remarks>
    [Fact]
    public void Eine_Bewertung_traegt_genau_diese_Felder()
    {
        Felder(typeof(Bewertung)).Should().BeEquivalentTo("Text", "Ausgang", "Am");

        Felder(typeof(BewertungV1)).Should().BeEquivalentTo("Outcome", "Text", "EvaluatedAt");
    }

    /// <summary>Und ein Ausgang kennt genau zwei Werte.</summary>
    /// <remarks>
    /// Zwei, nicht drei und nicht fünf: eine Skala mit drei Stufen ist eine Note
    /// mit Worten. „Weiter" oder „nicht weiter" ist eine Aussage über den
    /// Vorgang, alles Feinere wäre eine über den Menschen.
    /// </remarks>
    [Fact]
    public void Ein_Ausgang_kennt_genau_zwei_Werte() =>
        Enum.GetNames<Ausgang>().Should().BeEquivalentTo("Angenommen", "Abgelehnt");

    /// <summary>Der Bestand kennt zwei Fragen — und keine dritte.</summary>
    /// <remarks>
    /// <para>„Die Vorgänge dieser Firma" und „meine Vorgänge". Eine Methode
    /// „die Vorgänge <em>dieser Person</em>", von einer Firma aufrufbar, wäre
    /// die Auskunftei aus ADR-0042 §1 — und sie gibt es hier nicht, damit sie
    /// niemand vorfindet.</para>
    ///
    /// <para>Am Port und nicht an der Umsetzung: wer eine zweite Umsetzung
    /// schreibt, kommt an dieser Menge nicht vorbei.</para>
    /// </remarks>
    [Fact]
    public void Der_Bestand_kennt_genau_diese_Fragen() =>
        typeof(IVorgangsspeicher).GetMethods().Select(glied => glied.Name)
            .Should().BeEquivalentTo(
                "HoleAsync", "FuerPersonAsync", "FuerFirmaAsync", "SichereAsync", "LoescheAsync");

    /// <summary>Ein fremdes Unternehmen sieht nichts — dieselbe 404 wie „gibt es nicht".</summary>
    [Fact]
    public async Task Ein_fremdes_Unternehmen_sieht_den_Vorgang_nicht()
    {
        var (vorgaenge, tor, _, uhr) = Aufbau();
        var vorgang = await Stelle(vorgaenge, tor, uhr);

        // Auch mit Freigabe an das fremde Unternehmen: der Vorgang gehoert ihm
        // nicht, und eine Bewertung reist nie zu einem zweiten Unternehmen.
        tor.Stelle(Anna, Fremde, true);

        var handler = new VorgangHandler(vorgaenge, tor);

        await FluentActions
            .Awaiting(() => handler.Handle(
                new VorgangAbfrage(vorgang.Id, new SubjectId(Fremde), new TenantId(Fremde)),
                default))
            .Should().ThrowAsync<KeinVorgang>();
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 2 — DIE PERSON SIEHT DIE BEWERTUNG, IMMER
    // ---------------------------------------------------------------------

    /// <summary>Eine Absage ohne Text wird abgewiesen.</summary>
    /// <remarks>
    /// <strong>Der Kern der zweiten Auflage.</strong> Ablehnen und Begründen
    /// sind ein Schritt und nicht zwei — der zweite liesse sich sonst weglassen,
    /// und genau das ist der Tausch, den dieser Dienst nicht vermitteln darf:
    /// Arbeit gegen Schweigen.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Eine_Absage_ohne_Text_wird_abgewiesen(string? text) =>
        FluentActions.Invoking(() => Bewertung.Schreibe(text, Ausgang.Abgelehnt, Jetzt))
            .Should().Throw<Eingabefehler>();

    /// <summary>Und eine Zusage ohne Text ebenso — die Regel kennt keinen Ausnahmefall.</summary>
    [Fact]
    public void Eine_Zusage_ohne_Text_wird_ebenso_abgewiesen() =>
        FluentActions.Invoking(() => Bewertung.Schreibe(" ", Ausgang.Angenommen, Jetzt))
            .Should().Throw<Eingabefehler>();

    /// <summary>Die Gegenprobe: mit Text geht es.</summary>
    [Fact]
    public void Mit_Text_wird_bewertet() =>
        Bewertung.Schreibe("Sauber gelöst, nur die Fehlerbehandlung fehlt.",
            Ausgang.Abgelehnt, Jetzt).Text.Should().StartWith("Sauber");

    /// <summary>Die Personenseite fragt den Ledger nie.</summary>
    /// <remarks>
    /// Weder für die Liste noch für den einzelnen Vorgang. Die eigene
    /// Einwilligung zu prüfen, um die eigene Arbeit zu sehen, wäre nicht nur ein
    /// überflüssiger Sprung — wer widerrufen hat, käme sonst an seine
    /// Bewertungen nicht mehr heran.
    /// </remarks>
    [Fact]
    public async Task Die_Personenseite_fragt_den_Ledger_nie()
    {
        var (vorgaenge, tor, _, uhr) = Aufbau();
        var vorgang = await Stelle(vorgaenge, tor, uhr);
        var vorher = tor.Fragen;

        await new MeineVorgaengeHandler(vorgaenge).Handle(
            new MeineVorgaengeAbfrage(new SubjectId(Anna)), default);

        await new VorgangHandler(vorgaenge, tor).Handle(
            new VorgangAbfrage(vorgang.Id, new SubjectId(Anna), null), default);

        tor.Fragen.Should().Be(vorher, "die eigene Arbeit braucht keine Freigabe");
    }

    /// <summary>Nach einem Widerruf liest die Person weiter — das Unternehmen nicht.</summary>
    /// <remarks>
    /// <strong>Der schärfste Satz dieses Dienstes.</strong> Eine Bewertung, aus
    /// der man sich aussperren kann, indem man Sichtbarkeit zurücknimmt, wäre
    /// eine Beurteilung, die der Beurteilte nie liest — genau das, was hier
    /// nicht gebaut wird. Und die andere Hälfte gilt genauso: ein Widerruf
    /// wirkt beim nächsten Lesen (ADR-0013).
    /// </remarks>
    [Fact]
    public async Task Nach_einem_Widerruf_liest_die_Person_weiter()
    {
        var (vorgaenge, tor, ausgang, uhr) = Aufbau();
        var vorgang = await Stelle(vorgaenge, tor, uhr);

        vorgang.Reiche_ein(Einreichung.Schreibe("Fertig.", null, uhr.GetUtcNow()), uhr.GetUtcNow());

        await new BewertenHandler(vorgaenge, tor, ausgang, uhr).Handle(
            new BewertenBefehl(vorgang.Id, new TenantId(Firma), Ausgang.Abgelehnt, "Zu knapp."),
            default);

        // Und jetzt nimmt sie ihre Sichtbarkeit zurueck.
        tor.Stelle(Anna, Firma, false);

        var handler = new VorgangHandler(vorgaenge, tor);

        var ihrer = await handler.Handle(
            new VorgangAbfrage(vorgang.Id, new SubjectId(Anna), null), default);

        ihrer.Bewertung!.Text.Should().Be("Zu knapp.");

        await FluentActions
            .Awaiting(() => handler.Handle(
                new VorgangAbfrage(vorgang.Id, new SubjectId(Guid.NewGuid()), new TenantId(Firma)),
                default))
            .Should().ThrowAsync<KeinVorgang>();
    }

    /// <summary>Die Firmenliste fragt den Ledger bei jedem Lesen erneut.</summary>
    [Fact]
    public async Task Die_Firmenliste_fragt_den_Ledger_bei_jedem_Lesen()
    {
        var (vorgaenge, tor, _, uhr) = Aufbau();
        await Stelle(vorgaenge, tor, uhr);

        var handler = new FirmenvorgaengeHandler(vorgaenge, tor);
        var vorher = tor.Fragen;

        (await handler.Handle(new FirmenvorgaengeAbfrage(new TenantId(Firma)), default))
            .Should().ContainSingle();

        tor.Fragen.Should().Be(vorher + 1);

        // Widerruf — und schon die naechste Liste ist leer, nicht die
        // uebernaechste.
        tor.Stelle(Anna, Firma, false);

        (await handler.Handle(new FirmenvorgaengeAbfrage(new TenantId(Firma)), default))
            .Should().BeEmpty();

        tor.Fragen.Should().Be(vorher + 2);
    }

    /// <summary>Eine Bewertung entsteht nur zusammen mit ihrem Vermerk.</summary>
    /// <remarks>
    /// Ohne den Vermerk erführe die Person von einer Rückmeldung erst beim
    /// nächsten Vorbeischauen. Beides steht in <em>einer</em> Transaktion
    /// (<c>TransaktionsBehavior</c>); dass beides überhaupt geschieht, misst
    /// dieser Test.
    /// </remarks>
    [Fact]
    public async Task Eine_Bewertung_vermerkt_dass_die_Person_sie_lesen_soll()
    {
        var (vorgaenge, tor, ausgang, uhr) = Aufbau();
        var vorgang = await Stelle(vorgaenge, tor, uhr);

        vorgang.Reiche_ein(Einreichung.Schreibe("Fertig.", null, uhr.GetUtcNow()), uhr.GetUtcNow());
        ausgang.Vermerke.Clear();

        await new BewertenHandler(vorgaenge, tor, ausgang, uhr).Handle(
            new BewertenBefehl(vorgang.Id, new TenantId(Firma), Ausgang.Abgelehnt, "Zu knapp."),
            default);

        ausgang.Vermerke.Should().ContainSingle()
            .Which.Should().Be((Anna, Benachrichtigungsarten.Bewegung));
    }

    /// <summary>Bewertet wird einmal.</summary>
    /// <remarks>
    /// Eine Bewertung, die sich nachträglich ändern lässt, ist eine, die die
    /// Person gelesen haben kann, bevor sie ihren endgültigen Wortlaut bekam —
    /// und was sie gelesen hat, wäre dann nicht mehr nachweisbar.
    /// </remarks>
    [Fact]
    public void Bewertet_wird_einmal()
    {
        var vorgang = Vorgang.Stelle(
            new SubjectId(Anna), new TenantId(Firma), Probeaufgabe(), Jetzt);

        vorgang.Reiche_ein(Einreichung.Schreibe("Fertig.", null, Jetzt), Jetzt);
        vorgang.Bewerte(Bewertung.Schreibe("Gut.", Ausgang.Angenommen, Jetzt), Jetzt);

        FluentActions.Invoking(() =>
                vorgang.Bewerte(Bewertung.Schreibe("Doch nicht.", Ausgang.Abgelehnt, Jetzt), Jetzt))
            .Should().Throw<SchrittNichtMoeglich>();
    }

    /// <summary>Ohne Einreichung gibt es nichts zu bewerten.</summary>
    /// <remarks>
    /// Eine Bewertung ohne Arbeit wäre eine Aussage über den Menschen statt über
    /// seine Arbeit.
    /// </remarks>
    [Fact]
    public void Ohne_Einreichung_gibt_es_nichts_zu_bewerten() =>
        FluentActions.Invoking(() =>
                Vorgang.Stelle(new SubjectId(Anna), new TenantId(Firma), Probeaufgabe(), Jetzt)
                    .Bewerte(Bewertung.Schreibe("Gut.", Ausgang.Angenommen, Jetzt), Jetzt))
            .Should().Throw<SchrittNichtMoeglich>();

    // ---------------------------------------------------------------------
    // AUFLAGE 3 — DER UMFANG STEHT VORNE, ABLEHNEN WIRD NIRGENDS VERMERKT
    // ---------------------------------------------------------------------

    /// <summary>Ein Umfang ausserhalb von eins bis acht ist keine Arbeitsprobe.</summary>
    /// <remarks>
    /// Mehr als ein Arbeitstag ist keine Probe mehr, sondern Arbeit — und für
    /// Arbeit gibt es einen Vertrag und kein Formular. Die Plattform bietet für
    /// „sechzehn Stunden" schlicht kein Feld an.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(40)]
    public void Ein_Umfang_ausserhalb_der_Grenzen_wird_abgewiesen(int stunden) =>
        FluentActions.Invoking(() => Aufgabe.Schreibe(
                "Kleiner Dienst", "Bau etwas Kleines.", stunden, Jetzt.AddDays(7), Jetzt))
            .Should().Throw<Eingabefehler>();

    /// <summary>Die Gegenprobe: acht geht, neun nicht.</summary>
    [Fact]
    public void Acht_Stunden_gehen_gerade_noch()
    {
        Aufgabe.Schreibe("Kleiner Dienst", "Bau etwas Kleines.", 8, Jetzt.AddDays(7), Jetzt)
            .Stunden.Should().Be(8);

        Aufgabe.HoechsterUmfang.Should().Be(8, "ein Arbeitstag ist die Grenze, kein Startwert");
    }

    /// <summary>Eine Frist unter zwei Tagen wird abgewiesen.</summary>
    /// <remarks>
    /// Eine Aufgabe für morgen früh misst nicht, wie jemand arbeitet, sondern ob
    /// er gerade alles andere stehen lassen kann — und das hat mit Können nichts
    /// zu tun und mit Lebensumständen alles.
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(47)]
    public void Eine_zu_kurze_Frist_wird_abgewiesen(int stunden) =>
        FluentActions.Invoking(() => Aufgabe.Schreibe(
                "Kleiner Dienst", "Bau etwas Kleines.", 4, Jetzt.AddHours(stunden), Jetzt))
            .Should().Throw<Eingabefehler>();

    /// <summary>Und die Gegenprobe bei achtundvierzig.</summary>
    [Fact]
    public void Achtundvierzig_Stunden_gehen_gerade_noch() =>
        Aufgabe.Schreibe("Kleiner Dienst", "Bau etwas Kleines.", 4, Jetzt.AddHours(48), Jetzt)
            .Frist.Should().Be(Jetzt.AddHours(48));

    /// <summary>Die Meldung nennt die Regel und nie den Wert.</summary>
    /// <remarks>
    /// <c>ValidationBehavior</c> protokolliert Fehlermeldungen. Ein
    /// Aufgabentext oder eine Bewertung in einer Meldung stünde damit im
    /// Protokoll — und beide handeln von einem Menschen.
    /// </remarks>
    [Fact]
    public void Eine_Meldung_nennt_die_Regel_und_nie_den_Wert()
    {
        var fehler = Assert.Throws<Eingabefehler>(() => Aufgabe.Schreibe(
            "Geheimer Titel", "Geheimer Aufgabentext", 4, Jetzt.AddHours(1), Jetzt));

        fehler.Message.Should().NotContain("Geheimer");
    }

    /// <summary>Ein Stand kennt vier Werte, und keiner davon heisst „abgelehnt".</summary>
    /// <remarks>
    /// <strong>Die dritte Auflage als Typprobe.</strong> Wer nicht will, tut
    /// nichts — es gibt keinen Zustand, in dem das vermerkt wäre.
    /// </remarks>
    [Fact]
    public void Kein_Stand_heisst_abgelehnt() =>
        Enum.GetNames<Stand>().Should().BeEquivalentTo(
            "Gestellt", "Eingereicht", "Bewertet", "Abgelaufen");

    /// <summary>Und es gibt nirgends einen Weg, abzulehnen.</summary>
    /// <remarks>
    /// <para>Gescannt werden Anwendungsschicht und Domäne: dort stünde der
    /// Befehl. Ein höflicher Absageknopf wäre freundlicher zum Unternehmen und
    /// erzeugte genau den Vermerk, den ADR-0042 §3 verbietet — „hat dreimal
    /// abgelehnt" ist eine Tatsache über einen Menschen, sie entsteht aus lauter
    /// einzelnen berechtigten Klicks, und sie ist danach da.</para>
    ///
    /// <para><c>Abgelehnt</c> als <em>Ausgang</em> ist etwas anderes und
    /// deshalb hier nicht gemeint: das ist die Antwort des Unternehmens auf die
    /// Arbeit, nicht die Entscheidung der Person über die Aufgabe. Der Scan
    /// trifft Verben, nicht dieses Substantiv.</para>
    /// </remarks>
    [Theory]
    [InlineData("decline")]
    [InlineData("reject")]
    [InlineData("refuse")]
    [InlineData("ablehnen")]
    [InlineData("absagen")]
    [InlineData("verweiger")]
    public void Es_gibt_keinen_Weg_abzulehnen(string wort)
    {
        string[] namen =
        [
            .. Namen(typeof(AufgabeStellenBefehl).Assembly),
            .. Namen(typeof(Vorgang).Assembly)
        ];

        namen.Should().NotContain(
            name => name.Contains(wort, StringComparison.OrdinalIgnoreCase),
            $"'{wort}' wäre der Vermerk, den ADR-0042 §3 ausschliesst");
    }

    /// <summary>Verstrichen ist verstrichen — egal, warum.</summary>
    /// <remarks>
    /// „Wollte nicht", „hat es nicht geschafft" und „war krank" sind derselbe
    /// Stand: <c>expired</c>. Es gibt kein Feld, das sie unterscheidet, und
    /// deshalb auch keine Auskunft darüber.
    /// </remarks>
    [Fact]
    public void Verstrichen_ist_verstrichen()
    {
        var vorgang = Vorgang.Stelle(
            new SubjectId(Anna), new TenantId(Firma), Probeaufgabe(), Jetzt);

        vorgang.Stand_am(Jetzt).Should().Be(Stand.Gestellt);
        vorgang.Stand_am(Jetzt.AddDays(8)).Should().Be(Stand.Abgelaufen);

        // Und danach geht nichts mehr hinein: eine Aufgabe ohne Ende ist eine,
        // die in jemandes Kopf fuer immer offen bleibt.
        FluentActions.Invoking(() => vorgang.Reiche_ein(
                Einreichung.Schreibe("Doch noch.", null, Jetzt.AddDays(8)), Jetzt.AddDays(8)))
            .Should().Throw<SchrittNichtMoeglich>();
    }

    /// <summary>Wer geliefert hat, gilt nicht als abgelaufen.</summary>
    /// <remarks>
    /// Die Reihenfolge der Fragen in <c>Stand_am</c> ist die Aussage: eine
    /// abgelaufene Frist macht einen eingereichten Vorgang nicht wieder
    /// zunichte, und das Unternehmen schuldet die Antwort auch danach noch.
    /// </remarks>
    [Fact]
    public void Wer_geliefert_hat_laeuft_nicht_ab()
    {
        var vorgang = Vorgang.Stelle(
            new SubjectId(Anna), new TenantId(Firma), Probeaufgabe(), Jetzt);

        vorgang.Reiche_ein(Einreichung.Schreibe("Fertig.", null, Jetzt), Jetzt);

        vorgang.Stand_am(Jetzt.AddDays(8)).Should().Be(Stand.Eingereicht);
    }

    // ---------------------------------------------------------------------
    // WAS DIESER DIENST NICHT TUT
    // ---------------------------------------------------------------------

    /// <summary>Es gibt keine KI-Naht — und ausdrücklich keine für die Bewertung.</summary>
    /// <remarks>
    /// Ein Modell, das die Arbeit eines Menschen beurteilt, wäre die Black Box
    /// mit Komma aus ADR-0022, nur in Prosa. Es gibt in diesem Dienst keinen
    /// Port dafür, und damit auch keine Frage, was ein Modell sagen dürfte.
    /// <para>
    /// <c>entwurf</c> steht nicht auf der Liste, und das ist kein Versehen:
    /// <c>ZurEntwurfszeit</c> heisst so, weil <c>dotnet ef</c> zur Entwurfszeit
    /// läuft. Ein Wort, das zwei Dinge heisst, taugt nicht als Wächter —
    /// gesucht wird der <em>Port</em>, nicht die Silbe.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("entwerfer")]
    [InlineData("draft")]
    [InlineData("prompt")]
    [InlineData("modell")]
    public void Es_gibt_keine_KI_Naht(string wort)
    {
        string[] namen =
        [
            .. Namen(typeof(AufgabeStellenBefehl).Assembly),
            .. Namen(typeof(Vorgang).Assembly),
            .. Namen(typeof(AssessmentDbContext).Assembly)
        ];

        namen.Should().NotContain(
            name => name.Contains(wort, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Nach draussen gehen genau zwei Türen, und beide sind benannt.</summary>
    /// <remarks>
    /// <para>Der Ledger und notification-service — beide stehen in
    /// <c>docker-compose.yml</c>, weil die Egress-Grenze ihre erlaubten Hosts
    /// aus der Konfiguration ableitet.</para>
    ///
    /// <para><strong>Die eingereichte Adresse gehört nicht dazu.</strong> Sie
    /// wird gespeichert und angezeigt, nie abgerufen. Ein dritter Typ mit einer
    /// <c>IHttpClientFactory</c> wäre der erste Verdacht — und dieser Test
    /// zwingt, ihn hinzuschreiben.</para>
    /// </remarks>
    [Fact]
    public void Nach_draussen_gehen_genau_zwei_Tueren() =>
        typeof(AssessmentDbContext).Assembly.GetExportedTypes()
            .Where(typ => typ.GetConstructors().Any(bau => bau.GetParameters()
                .Any(glied => glied.ParameterType == typeof(IHttpClientFactory))))
            .Select(typ => typ.Name)
            .Should().BeEquivalentTo("HttpEinwilligungstor", "HttpBenachrichtigung");

    /// <summary>Eine Adresse ist http oder https — sonst ist sie keine.</summary>
    /// <remarks>
    /// <c>javascript:</c> und <c>data:</c> stünden sonst als Verweis in einer
    /// fremden Oberfläche: gespeichert von einem Menschen, geklickt von einem
    /// anderen.
    /// </remarks>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("nicht mal eine Adresse")]
    public void Eine_Adresse_ist_http_oder_gar_nichts(string adresse) =>
        FluentActions.Invoking(() => Einreichung.Schreibe(null, adresse, Jetzt))
            .Should().Throw<Eingabefehler>();

    /// <summary>Die Gegenprobe.</summary>
    [Fact]
    public void Eine_http_Adresse_geht_durch() =>
        Einreichung.Schreibe(null, "https://beispiel.test/loesung", Jetzt)
            .Adresse.Should().Be("https://beispiel.test/loesung");

    /// <summary>Eine leere Einreichung ist gar keine.</summary>
    [Fact]
    public void Eine_leere_Einreichung_ist_keine() =>
        FluentActions.Invoking(() => Einreichung.Schreibe("  ", null, Jetzt))
            .Should().Throw<Eingabefehler>();

    // ---------------------------------------------------------------------

    /// <summary>Die öffentlichen Instanzeigenschaften eines Typs.</summary>
    private static string[] Felder(Type typ) =>
        [.. typ.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(glied => glied.Name != "EqualityContract")
            .Select(glied => glied.Name)];

    private static string[] Namen(Assembly assembly) =>
        [.. assembly.GetExportedTypes()
            .SelectMany(typ => typ.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(glied => $"{typ.Name}.{glied.Name}")
                .Append(typ.Name))
            .Distinct(StringComparer.Ordinal)];

    /// <summary>Jede Spalte, die dieses Modell wirklich anlegt.</summary>
    private static string[] Spalten()
    {
        var bauer = new DbContextOptionsBuilder<AssessmentDbContext>();
        AssessmentDbContextFactory.ZurEntwurfszeit(bauer, "Host=pruefstand;Database=assessment");

        using var kontext = new AssessmentDbContext(bauer.Options);

        return [.. kontext.Model.GetEntityTypes()
            .SelectMany(typ => typ.GetProperties())
            .Select(eigenschaft => eigenschaft.GetColumnName())
            .Distinct(StringComparer.Ordinal)];
    }

    private static Aufgabe Probeaufgabe() =>
        Aufgabe.Schreibe(
            "Kleiner Dienst", "Bau einen kleinen Dienst.", 4, Jetzt.AddDays(7), Jetzt);

    private static (Probevorgaenge, Probetor, Probeausgang, Probeuhr) Aufbau() =>
        (new Probevorgaenge(), new Probetor(), new Probeausgang(), new Probeuhr(Jetzt));

    private static async Task<Vorgang> Stelle(
        Probevorgaenge vorgaenge, Probetor tor, Probeuhr uhr)
    {
        tor.Stelle(Anna, Firma, true);

        return await new AufgabeStellenHandler(vorgaenge, tor, new Probeausgang(), uhr).Handle(
            new AufgabeStellenBefehl(
                new SubjectId(Anna),
                new TenantId(Firma),
                "Kleiner Dienst",
                "Bau einen kleinen Dienst.",
                4,
                uhr.GetUtcNow().AddDays(7)),
            default);
    }
}
