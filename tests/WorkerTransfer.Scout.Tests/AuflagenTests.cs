using System.Reflection;
using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Scout.Application.Ansprache;
using WorkerTransfer.Scout.Application.Kandidaten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Suchen;
using WorkerTransfer.Scout.Domain.Treffer;

namespace WorkerTransfer.Scout.Tests;

/// <summary>
/// Die vier Auflagen aus ADR-0036, jede als Test.
/// </summary>
/// <remarks>
/// <para>Sie stehen in <em>einer</em> Datei, und das ist Absicht: es sind die
/// vier Sätze, um derentwillen dieser Dienst gebaut werden durfte. Wer einen
/// davon rot macht, hat nicht einen Test gebrochen, sondern die Begründung des
/// ganzen Dienstes.</para>
///
/// <para>Geprüft wird, wo möglich, an <em>Typen</em> statt an Verhalten: eine
/// Verhaltensprobe beantwortet „tut es das heute nicht?", eine Typprobe
/// verlangt, dass wer es ändert, die Menge hier hinschreibt. Dieselbe Bewegung,
/// aber eine, die auffällt.</para>
/// </remarks>
public class AuflagenTests
{
    private static readonly TenantId Firma = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    // ---------------------------------------------------------------------
    // AUFLAGE 1 — KEINE SORTIERUNG NACH PASSUNG
    // ---------------------------------------------------------------------

    /// <summary>Zwei gleiche Suchen liefern dieselbe Reihenfolge.</summary>
    [Fact]
    public async Task Zwei_gleiche_Suchen_liefern_dieselbe_Reihenfolge()
    {
        var (handler, suche, tor, _) = Aufbau();

        var drei = Menschen(3);
        suche.Bestand.AddRange(drei.Select(wer => Profil(wer, ["Python"])));
        Frei(tor, drei);

        var erste = await handler.Handle(Frage(), default);
        var zweite = await handler.Handle(Frage(), default);

        erste.Eintraege.Select(t => t.Wer).Should().Equal(zweite.Eintraege.Select(t => t.Wer));
    }

    /// <summary>
    /// Wer mehr der gesuchten Worte nennt, rückt NICHT nach vorn.
    /// </summary>
    /// <remarks>
    /// <para>Der eigentliche Test dieser Auflage, und die Gegenprobe steckt in
    /// den Daten: der Letzte der Liste erfüllt beide gesuchten Worte, der Erste
    /// nur eines. Eine Sortierung nach Passung müsste ihn nach vorn ziehen —
    /// und wer eine einbaut, macht genau diese Zeile rot.</para>
    ///
    /// <para>Die Reihenfolge ist die von profile-service
    /// (<c>updated_at DESC, id DESC</c>): stabil und <em>sachfremd</em>. Aus ihr
    /// folgt über niemanden ein Rang, und das ist der Grund, sie unangetastet
    /// durchzureichen.</para>
    /// </remarks>
    [Fact]
    public async Task Wer_mehr_erfuellt_rueckt_nicht_nach_vorn()
    {
        var (handler, suche, tor, _) = Aufbau();

        var wenig = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var viel = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

        // `wenig` steht vorn und erfuellt EINES der zwei gesuchten Worte;
        // `viel` steht hinten und erfuellt BEIDE. Eine Sortierung nach Passung
        // muesste `viel` nach vorn ziehen — und macht damit diese Zeile rot.
        suche.Bestand.Add(Profil(wenig, ["Python", "Go"]));
        suche.Bestand.Add(Profil(viel, ["Python", "Kubernetes"]));
        Frei(tor, [wenig, viel]);

        var seite = await handler.Handle(Frage("Python", "Kubernetes"), default);

        seite.Eintraege.Select(t => t.Wer.Value).Should().Equal(
            [wenig, viel],
            "die Reihenfolge ist die von profile-service — wer beide gesuchten "
            + "Worte nennt, steht nicht deshalb vor dem, der eines nennt");

        // Und die Haekchen sagen, WELCHES fehlt — das ist die Auskunft, die
        // eine Zahl verbergen wuerde.
        seite.Eintraege[0].Haken.Should().Equal(
            new Haken("Python", true), new Haken("Kubernetes", false));
        seite.Eintraege[1].Haken.Should().Equal(
            new Haken("Python", true), new Haken("Kubernetes", true));
    }

    /// <summary>Auch die Häkchen bleiben in der Reihenfolge der Suche.</summary>
    /// <remarks>
    /// Die Haken nach „erfüllt zuerst" zu ordnen wäre eine Sortierung nach
    /// Passung im Kleinen — und der Anfang derselben im Grossen.
    /// </remarks>
    [Fact]
    public async Task Die_Haken_stehen_in_der_Reihenfolge_der_Suche()
    {
        var (handler, suche, tor, _) = Aufbau();

        var wer = Menschen(1)[0];
        suche.Bestand.Add(Profil(wer, ["Go"]));
        Frei(tor, [wer]);

        var seite = await handler.Handle(Frage("Rust", "Go"), default);

        seite.Eintraege.Should().ContainSingle()
            .Which.Haken.Select(h => (h.Wort, h.Genannt))
            .Should().Equal(("Rust", false), ("Go", true));
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 2 — KEINE ZAHL ÜBER EINEN MENSCHEN
    // ---------------------------------------------------------------------

    /// <summary>Worte, die ein Urteil über einen Menschen ankündigen.</summary>
    /// <remarks>
    /// Die Liste aus ADR-0036 Auflage 2, plus die Zwillinge, die dasselbe unter
    /// anderem Namen wären. <c>anzahl</c> steht darauf, und deshalb heisst die
    /// Seitenlänge in diesem Dienst <c>Seitenlaenge</c> und nicht
    /// <c>Anzahl</c> — ein Feld, das heute eine Seitengrösse ist, ist morgen
    /// die Zahl, die jemand neben einen Menschen schreibt.
    /// </remarks>
    private static readonly string[] Verboten =
    [
        "score", "punkt", "rank", "rang", "weight", "gewicht", "fit", "passung",
        "percent", "prozent", "quote", "anzahl", "bewert", "note", "level",
        "aktivitaet", "activity", "match"
    ];

    /// <summary>
    /// Keine öffentliche Fläche der Domäne oder der Verträge trägt einen solchen
    /// Namen.
    /// </summary>
    /// <remarks>
    /// Der Zwilling von <c>Adr0022Tests</c> aus github-service, hier über die
    /// beiden Assemblies, in denen eine Zahl entstünde (Domäne) und hinausginge
    /// (Verträge). Wer eines der Worte braucht, hat die Grenze überschritten
    /// oder muss diesen Test ändern — und das ist ein sichtbarer Commit, den
    /// jemand begründen muss.
    /// </remarks>
    [Fact]
    public void Nichts_hier_fasst_einen_Menschen_in_einer_Zahl_zusammen()
    {
        var flaechen = new[] { typeof(Treffer).Assembly, typeof(TrefferV1).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(typ => typ.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(glied => $"{typ.Name}.{glied.Name}")
                .Append(typ.Name))
            .Distinct()
            .ToList();

        flaechen.Should().NotBeEmpty("sonst prüft der Test nichts");

        foreach (var name in flaechen)
        {
            foreach (var wort in Verboten)
            {
                name.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{name}' trägt '{wort}' — ADR-0022, ADR-0036 Auflage 2");
            }
        }
    }

    /// <summary>Die Erkennung selbst stimmt — sonst wäre der Test grün, weil er nichts findet.</summary>
    [Theory]
    [InlineData("Haken", false)]
    [InlineData("Belegstand", false)]
    [InlineData("Passungsgrad", true)]
    [InlineData("MatchScore", true)]
    [InlineData("Trefferanzahl", true)]
    public void Die_Erkennung_trennt_richtig(string name, bool erwartet) =>
        Verboten.Any(wort => name.Contains(wort, StringComparison.OrdinalIgnoreCase))
            .Should().Be(erwartet);

    /// <summary>Ein Treffer trägt eine Häkchen<em>liste</em> und keine Summe.</summary>
    /// <remarks>
    /// Die geschlossene Feldmenge, nicht eine Verbotsliste: wer ein Feld
    /// ergänzt, schreibt es hier hin — und beantwortet dabei, ob es
    /// abgeschrieben oder gerechnet ist. Nur das Erste darf dazu.
    /// </remarks>
    [Fact]
    public void Ein_Treffer_traegt_genau_diese_Felder()
    {
        Felder(typeof(TrefferV1)).Should().BeEquivalentTo(
            "SubjectId", "Headline", "Location", "RemoteOk",
            "Named", "Checks", "Evidence", "EvidenceState");

        Felder(typeof(HakenV1)).Should().BeEquivalentTo("Word", "Named");
    }

    /// <summary>Und eine Seite trägt keine Gesamtzahl.</summary>
    /// <remarks>
    /// ADR-0026: sie verriete über die Differenz zur Seitenlänge, wie viele
    /// Profile <em>nicht</em> freigegeben sind. Das ist genau die Auskunft, die
    /// der Ledger schützt.
    /// </remarks>
    [Fact]
    public void Eine_Seite_traegt_keine_Gesamtzahl()
    {
        Felder(typeof(TrefferseiteV1)).Should().BeEquivalentTo("Items", "Next");
        Felder(typeof(Trefferseite)).Should().BeEquivalentTo("Eintraege", "Weiter");
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 3 — NUR GENANNTES IST DURCHSUCHBAR
    // ---------------------------------------------------------------------

    /// <summary>Ein nur BELEGTES Wort findet niemanden.</summary>
    /// <remarks>
    /// <para>Die Auflage in einem Satz: „kubernetes" steht als GitHub-Topic an
    /// einem Repository dieser Person und in keinem Profil. Eine Suche danach
    /// findet sie nicht — ein Topic ist eine Aussage über ein <em>Artefakt</em>,
    /// und wer danach suchte, machte sie stillschweigend zu einer über den
    /// Menschen (ADR-0033).</para>
    ///
    /// <para>Die Gegenprobe steht in derselben Reihe: dieselbe Person ist über
    /// ihr <em>genanntes</em> „Go" sehr wohl zu finden, und der Beleg taucht
    /// dann am Treffer auf. Ohne diese zweite Hälfte wäre der Test auch grün,
    /// wenn die Suche gar nichts fände.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_nur_belegtes_Wort_findet_niemanden()
    {
        var (handler, suche, tor, belege) = Aufbau();

        var wer = Menschen(1)[0];
        suche.Bestand.Add(Profil(wer, ["Go"]));
        Frei(tor, [wer]);
        belege.Bestand[wer] = new Belegbogen(
            [new Beleg("kubernetes", Belegart.Thema, "infra", "https://example.test/infra")],
            Belegstand.Vollstaendig);

        var ueberDenBeleg = await handler.Handle(Frage("Kubernetes"), default);

        ueberDenBeleg.Eintraege.Should().BeEmpty(
            "ein Beleg ist eine Aussage über ein Artefakt und macht niemanden auffindbar");

        var ueberDasGenannte = await handler.Handle(Frage("Go"), default);

        ueberDasGenannte.Eintraege.Should().ContainSingle()
            .Which.Belege.Should().ContainSingle().Which.Wort.Should().Be(
                "kubernetes", "zum Treffer DAZUGEHOLT wird er sehr wohl");
    }

    /// <summary>Die Belege werden erst NACH der Freigabe geholt.</summary>
    /// <remarks>
    /// Die Reihenfolge ist die Zusage: wer nicht freigegeben hat, über den wird
    /// auch nichts nachgeschlagen. Ein Belegabruf für eine verborgene Person
    /// wäre ein Aufruf, den es nicht geben darf — und er stünde im Protokoll
    /// des anderen Dienstes.
    /// </remarks>
    [Fact]
    public async Task Ueber_wen_nichts_freigegeben_ist_wird_nichts_nachgeschlagen()
    {
        var (handler, suche, tor, belege) = Aufbau();

        var sichtbar = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var verborgen = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        suche.Bestand.Add(Profil(sichtbar, ["Go"]));
        suche.Bestand.Add(Profil(verborgen, ["Go"]));
        Frei(tor, [sichtbar]);

        await handler.Handle(Frage("Go"), default);

        belege.Gefragt.Should().Equal(sichtbar);
    }

    /// <summary>Der Filter, der hinausgeht, trägt nur genannte Worte.</summary>
    /// <remarks>
    /// Am Typ und nicht am Verhalten: <see cref="Suchfilter"/> hat kein Feld für
    /// einen Beleg, also gibt es keinen Weg, einen mitzuschicken. Wer eines
    /// ergänzt, macht diese Zeile rot.
    /// </remarks>
    [Fact]
    public void Ein_Suchfilter_traegt_genau_diese_Felder() =>
        Felder(typeof(Suchfilter)).Should().BeEquivalentTo(
            "GenannteWorte", "Ort", "NurRemote");

    // ---------------------------------------------------------------------
    // AUFLAGE 4 — DIE ANSPRACHE IST EIN ENTWURF
    // ---------------------------------------------------------------------

    /// <summary>Der Ansprachekontext trägt vier Felder — und keines davon ist ein Beleg.</summary>
    /// <remarks>
    /// <para>Kein Name, keine E-Mail-Adresse, keine <c>SubjectId</c>, kein
    /// Arbeitgeber, kein Lebenslauf, kein Marktstatus — und <strong>kein Name
    /// des suchenden Unternehmens</strong>: ein Firmenname im Prompt wäre die
    /// Einladung, das Modell etwas über den Arbeitgeber sagen zu lassen, was
    /// niemand geprüft hat (ADR-0024).</para>
    ///
    /// <para>Und keine Belege. Sie sind sichtbar, aber nicht <em>genannt</em>;
    /// sie in eine Ansprache zu schreiben hiesse, sie der Person zuzuschreiben —
    /// der Schritt, den ADR-0033 ihr selbst vorbehält.</para>
    /// </remarks>
    [Fact]
    public void Der_Ansprachekontext_traegt_genau_vier_Felder() =>
        Felder(typeof(Ansprachelage)).Should().BeEquivalentTo(
            ["Ueberschrift", "Genannt", "Gesucht", "Wunsch", "Prompt"],
            "ADR-0036 Auflage 4 nennt diese Menge, und wer sie erweitert, "
            + "schickt etwas Neues über eine Person hinaus");

    /// <summary>Was wirklich hinausgeht, trägt keine Kennung und keine Adresse.</summary>
    [Fact]
    public async Task Der_Prompt_traegt_nichts_ueber_die_Person_hinaus()
    {
        var (_, suche, tor, _) = Aufbau();
        var entwerfer = new Probeentwerfer();
        var handler = new AnspracheHandler(tor, suche, entwerfer);

        var wer = Menschen(1)[0];
        suche.Bestand.Add(Profil(wer, ["Go"]));
        Frei(tor, [wer]);

        await handler.Handle(
            new AnspracheAbfrage(Firma, new SubjectId(wer), ["Go"], "kürzer"), default);

        var prompt = entwerfer.Letzte!.Prompt;

        prompt.Should().NotContain(wer.ToString());
        prompt.Should().NotContain(Firma.Value.ToString());
        prompt.Should().NotContain("@");
        prompt.Should().Contain("Go");
    }

    /// <summary>Dieser Dienst hat keinen Weg, jemandem zu schreiben.</summary>
    /// <remarks>
    /// <para>Der Test, der die vierte Auflage wirklich trägt — und er prüft
    /// eine <em>Abwesenheit</em>. Die Anwendungsschicht kennt genau einen Port
    /// nach draussen, über den etwas an einen Menschen ginge: den Postausgang
    /// mit „dein Profil wurde entdeckt". Es gibt keinen Postboten, keine
    /// Mailadresse, keinen Versand-Port und keinen Befehl, der eine Ansprache
    /// abschickte.</para>
    ///
    /// <para>Geprüft an den Typnamen und nicht am Verhalten: ein Verhaltenstest
    /// beantwortet „passiert es heute nicht?", dieser verlangt, dass wer einen
    /// Versandweg einbaut, ihn hier hinschreibt.</para>
    /// </remarks>
    [Fact]
    public void Es_gibt_keinen_Weg_jemandem_zu_schreiben()
    {
        var verdaechtig = new[] { "postbote", "versand", "mail", "smtp", "senden", "sender" };

        var namen = typeof(AnspracheAbfrage).Assembly
            .GetExportedTypes()
            .SelectMany(typ => typ.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(glied => $"{typ.Name}.{glied.Name}")
                .Append(typ.Name))
            .Distinct()
            .ToArray();

        namen.Should().NotBeEmpty("sonst prüft der Test nichts");

        foreach (var name in namen)
        {
            foreach (var wort in verdaechtig)
            {
                name.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{name}' sieht nach einem Versandweg aus — der Dienst "
                        + "entwirft, er schreibt niemandem (ADR-0036 Auflage 4)");
            }
        }
    }

    /// <summary>Der Entwurf trägt nur den Text — kein <c>sent_at</c>, keinen Empfänger.</summary>
    [Fact]
    public void Ein_Entwurf_traegt_nur_den_Text() =>
        Felder(typeof(AnspracheentwurfV1)).Should().BeEquivalentTo("Draft");

    // ---------------------------------------------------------------------

    /// <summary>Die öffentlichen Instanzeigenschaften eines Typs.</summary>
    /// <remarks>
    /// Statische bleiben draussen: <c>Regeln</c> ist der System-Prompt und kein
    /// Feld, das jemand befüllt. <c>Prompt</c> zählt dagegen mit, obwohl es
    /// abgeleitet ist — es ist die Zeichenkette, die wirklich hinausgeht.
    /// </remarks>
    private static string[] Felder(Type typ) =>
        [.. typ.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(e => e.Name)];

    private static (KandidatenHandler, Probesuche, Probetor, Probebelege) Aufbau()
    {
        var suche = new Probesuche();
        var tor = new Probetor();
        var belege = new Probebelege();

        return (new KandidatenHandler(suche, tor, belege), suche, tor, belege);
    }

    private static KandidatenAbfrage Frage(params string[] worte) =>
        new(Firma, Suchfilter.Aus(worte, null, false), 20, null);

    private static Guid[] Menschen(int wie_viele) =>
        [.. Enumerable.Range(1, wie_viele).Select(
            nummer => Guid.Parse($"cccccccc-0000-0000-0000-{nummer:D12}"))];

    private static Profilfund Profil(Guid wer, string[] genannt) =>
        new(new SubjectId(wer), "Entwicklerin", "Berlin", true, genannt);

    private static void Frei(Probetor tor, IEnumerable<Guid> wer)
    {
        foreach (var einer in wer)
        {
            tor.Frei.Add((einer, Firma.Value));
        }
    }
}
