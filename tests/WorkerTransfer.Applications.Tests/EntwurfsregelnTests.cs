using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Applications.Application.Entwuerfe;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Applications.Infrastructure.Anschreiben;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Die Regeln aus ADR-0034 — als Prüfungen, nicht als Absichtserklärung.</summary>
public sealed class EntwurfsregelnTests
{
    private static readonly DateTimeOffset Jetzt =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static Bewerbungsentwurf Fertig()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.Nimm_text_an("Bewerbung als Entwickler", "Sehr geehrte …", Jetzt);

        return entwurf;
    }

    /// <summary>
    /// <strong>Es gibt keinen Weg von „entsteht" nach „gesendet".</strong>
    /// </summary>
    /// <remarks>
    /// Zwischen dem Modell und dem Unternehmen stehen zwei Handlungen eines
    /// Menschen: freigeben und senden. Ein Knopf „freigeben und senden" spart
    /// einen Klick und nimmt der Freigabe ihren Sinn — sie ist der Moment, in
    /// dem jemand sagt „so, und nicht anders".
    /// </remarks>
    [Fact]
    public void Ohne_Freigabe_geht_nichts_hinaus()
    {
        var entwurf = Fertig();

        var versuch = () => entwurf.Vermerke_versand(Jetzt);

        versuch.Should().Throw<EntwurfsschrittNichtErlaubt>();
        entwurf.Stand.Should().Be(Entwurfsstand.Pruefen);
    }

    /// <summary>Und aus „entsteht" erst recht nicht.</summary>
    [Fact]
    public void Aus_dem_Entstehen_heraus_gibt_niemand_frei()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        var freigeben = () => entwurf.Gib_frei(Jetzt);
        var senden = () => entwurf.Vermerke_versand(Jetzt);

        freigeben.Should().Throw<EntwurfsschrittNichtErlaubt>();
        senden.Should().Throw<EntwurfsschrittNichtErlaubt>();
    }

    /// <summary>Eine offene Anmerkung verhindert die Freigabe.</summary>
    [Fact]
    public void Mit_offener_Anmerkung_gibt_niemand_frei()
    {
        var entwurf = Fertig();

        entwurf.Merke_an("Der zweite Absatz klingt hohl.", "Sehr geehrte", Jetzt);

        var versuch = () => entwurf.Gib_frei(Jetzt);

        versuch.Should().Throw<OffeneAnmerkungen>();
        entwurf.Stand.Should().Be(Entwurfsstand.Ueberarbeiten);
    }

    /// <summary>
    /// <strong>Ohne Auftrag wird nicht überarbeitet.</strong>
    /// </summary>
    /// <remarks>
    /// ADR-0024 verbietet die Reflexionsschleife — „zwei Aufrufe, um die
    /// eigenen Worte der Person <em>ungefragt</em> umzuschreiben". Ein Knopf,
    /// der ohne offene Anmerkung umschreibt, wäre genau das unter anderem
    /// Namen.
    /// </remarks>
    [Fact]
    public void Ohne_Anmerkung_wird_nicht_ueberarbeitet()
    {
        var entwurf = Fertig();

        var versuch = () => entwurf.Ueberarbeite("Neu", "Ganz anders", Jetzt);

        versuch.Should().Throw<OffeneAnmerkungen>();
        entwurf.Text.Should().Be("Sehr geehrte …");
        entwurf.Fassung.Should().Be(1);
    }

    /// <summary>Die Überarbeitung hakt ab und zählt hoch.</summary>
    /// <remarks>
    /// Die Fassungsnummer ist keine Zierde: wer nicht sieht, dass sich etwas
    /// geändert hat, prüft nicht wirklich.
    /// </remarks>
    [Fact]
    public void Die_Ueberarbeitung_hakt_ab_und_zaehlt_hoch()
    {
        var entwurf = Fertig();

        entwurf.Merke_an("Kürzer.", null, Jetzt);
        entwurf.Ueberarbeite("Bewerbung", "Kurz und gut.", Jetzt);

        entwurf.Stand.Should().Be(Entwurfsstand.Pruefen);
        entwurf.Fassung.Should().Be(2);
        entwurf.HatOffeneAnmerkungen.Should().BeFalse();

        // Die Anmerkung BLEIBT — sie ist der Beleg dafür, warum sich der Text
        // geändert hat. Sie zu löschen brächte die Fassungsgeschichte um ihre
        // Begründung.
        entwurf.Anmerkungen.Should().HaveCount(1);
        entwurf.Anmerkungen[0].Erledigt.Should().BeTrue();
    }

    /// <summary>Eine Anmerkung nach der Freigabe nimmt sie zurück.</summary>
    /// <remarks>
    /// Wer nach der Freigabe noch etwas findet, hat es gefunden. Eine Freigabe,
    /// die einen Widerspruch überlebt, ist keine.
    /// </remarks>
    [Fact]
    public void Eine_Anmerkung_nach_der_Freigabe_nimmt_sie_zurueck()
    {
        var entwurf = Fertig();

        entwurf.Gib_frei(Jetzt);
        entwurf.Stand.Should().Be(Entwurfsstand.Freigegeben);

        entwurf.Merke_an("Doch nicht.", null, Jetzt);

        entwurf.Stand.Should().Be(Entwurfsstand.Ueberarbeiten);

        var versuch = () => entwurf.Vermerke_versand(Jetzt);
        versuch.Should().Throw<EntwurfsschrittNichtErlaubt>();
    }

    /// <summary>Selbst zu ändern nimmt die Freigabe ebenfalls zurück.</summary>
    /// <remarks>
    /// Sonst sagte „freigegeben" nichts über den Text aus, der dann hinausgeht.
    /// </remarks>
    [Fact]
    public void Eigene_Aenderung_nach_der_Freigabe_nimmt_sie_zurueck()
    {
        var entwurf = Fertig();

        entwurf.Gib_frei(Jetzt);
        entwurf.Aendere_selbst("Bewerbung", "Ich habe noch etwas geändert.", Jetzt);

        entwurf.Stand.Should().Be(Entwurfsstand.Pruefen);
    }

    /// <summary>Ein leeres Anschreiben gibt niemand frei.</summary>
    [Fact]
    public void Ein_leeres_Anschreiben_geht_nicht_durch()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.Nimm_text_an("Betreff", "", Jetzt);

        var versuch = () => entwurf.Gib_frei(Jetzt);

        versuch.Should().Throw<AnmerkungFehler>();
    }

    /// <summary>
    /// Nach einem fehlgeschlagenen Schreiben darf die Person selbst schreiben.
    /// </summary>
    /// <remarks>
    /// Aus <c>Entsteht</c> ist <c>Aendere_selbst</c> verboten. Ohne Anbieter
    /// muss der Entwurf deshalb auf <c>Fehlgeschlagen</c> fallen — sonst bleibt
    /// die Zeile ewig „wird geschrieben" und der Brief bleibt leer.
    /// </remarks>
    [Fact]
    public void Nach_einem_fehlgeschlagenen_Schreiben_darf_man_selbst_aendern()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.Scheitere("Es ist kein Entwurfsanbieter eingerichtet.", Jetzt);
        entwurf.Aendere_selbst("Bewerbung", "Ich schreibe selbst.", Jetzt);

        entwurf.Stand.Should().Be(Entwurfsstand.Pruefen);
        entwurf.Text.Should().Be("Ich schreibe selbst.");
    }

    /// <summary>
    /// Nach einem Fehlschlag darf das Modell denselben Entwurf noch einmal
    /// füllen — sonst verfällt eine gelungene Antwort als 422.
    /// </summary>
    [Fact]
    public void Nach_einem_Fehlschlag_darf_das_Modell_nochmal_schreiben()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.Scheitere("Der Entwurfsanbieter antwortet mit 404.", Jetzt);
        entwurf.Nimm_text_an("Bewerbung", "Sehr geehrte Damen und Herren.", Jetzt);

        entwurf.Stand.Should().Be(Entwurfsstand.Pruefen);
        entwurf.Text.Should().Be("Sehr geehrte Damen und Herren.");
        entwurf.Fehler.Should().BeEmpty();
    }

    /// <summary>
    /// Ein Anschreiben bleibt kurz: 1500 Tokens haben ein lokales Modell
    /// über die Minute gedrückt, und länger als eine Minute wartet niemand.
    /// </summary>
    [Fact]
    public void Das_Anschreiben_fordert_keine_lange_Antwort()
    {
        HttpAnschreiber.HoechsteToken.Should().BeLessThanOrEqualTo(1500);
        Anschreibenkontext.Regeln.Should().Contain("250");
        Anschreibenkontext.Regeln.Should().Contain("Muttersprachler");
        Anschreibenkontext.Regeln.Should().Contain("Erfinde");
        Anschreibenkontext.Regeln.Should().Contain("Signatur");
    }

    [Fact]
    public void Ein_Bruchstueck_bleibt_Entsteht()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.Beginne_schreiben(Jetzt);
        var start = entwurf.SchreibenBegonnen;

        entwurf.Nimm_bruchstueck("Bewerbung", "Sehr geehrte", Jetzt.AddSeconds(12));

        entwurf.Stand.Should().Be(Entwurfsstand.Entsteht);
        entwurf.Text.Should().Be("Sehr geehrte");
        entwurf.Fassung.Should().Be(1);
        entwurf.SchreibenBegonnen.Should().Be(start);
    }

    [Fact]
    public void Das_erste_Schreiben_setzt_den_Start()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.SchreibenBegonnen.Should().BeNull();

        entwurf.Beginne_schreiben(Jetzt, leeren: false);

        entwurf.SchreibenBegonnen.Should().Be(Jetzt);
        entwurf.Stand.Should().Be(Entwurfsstand.Entsteht);
    }

    [Fact]
    public void Nach_einem_Fehlschlag_beginnt_das_Schreiben_wieder()
    {
        var entwurf = Bewerbungsentwurf.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        entwurf.Scheitere("Zeitüberschreitung", Jetzt);
        entwurf.Beginne_schreiben(Jetzt);

        entwurf.Stand.Should().Be(Entwurfsstand.Entsteht);
        entwurf.Fehler.Should().BeEmpty();
        entwurf.Text.Should().BeEmpty();
    }

    /// <summary>Der ganze Weg, mit jedem Sprung als eigener Handlung.</summary>
    [Fact]
    public void Der_ganze_Weg_verlangt_jeden_Schritt_einzeln()
    {
        var entwurf = Fertig();

        entwurf.Merke_an("Bitte konkreter.", "Sehr geehrte", Jetzt);
        entwurf.Stand.Should().Be(Entwurfsstand.Ueberarbeiten);

        entwurf.Ueberarbeite("Bewerbung", "Konkret.", Jetzt);
        entwurf.Stand.Should().Be(Entwurfsstand.Pruefen);

        entwurf.Gib_frei(Jetzt);
        entwurf.Stand.Should().Be(Entwurfsstand.Freigegeben);

        entwurf.Vermerke_versand(Jetzt);
        entwurf.Stand.Should().Be(Entwurfsstand.Gesendet);

        // Danach ist Schluss: ein gesendetes Anschreiben ändert niemand mehr.
        var nachtraeglich = () => entwurf.Aendere_selbst("x", "y", Jetzt);
        nachtraeglich.Should().Throw<EntwurfsschrittNichtErlaubt>();
    }

    /// <summary>
    /// <strong>Der Kontext trägt nur eigene Angaben und die Anzeige.</strong>
    /// </summary>
    /// <remarks>
    /// Dieselbe Prüfung, die ADR-0024 für <c>Anzeigenentwurf</c> und den
    /// Profilentwurf vorsieht: die Feldliste ist die Grenze. Wer hier ein Feld
    /// ergänzt, beantwortet zuerst — gehört es der bewerbenden Person, oder
    /// steht es in der öffentlichen Anzeige? Alles andere wäre eine Aussage
    /// über einen Dritten.
    /// </remarks>
    [Fact]
    public void Der_Kontext_traegt_nichts_ueber_Dritte()
    {
        typeof(Anschreibenkontext).GetProperties()
            .Select(eigenschaft => eigenschaft.Name)
            .Should().BeEquivalentTo(
                // Die Anzeige — öffentlich lesbar, ohne Anmeldung.
                nameof(Anschreibenkontext.StellenTitel),
                nameof(Anschreibenkontext.Unternehmen),
                nameof(Anschreibenkontext.StellenOrt),
                nameof(Anschreibenkontext.StellenBeschreibung),
                nameof(Anschreibenkontext.GesuchteFaehigkeiten),
                // Die eigenen Angaben.
                nameof(Anschreibenkontext.EigenerName),
                nameof(Anschreibenkontext.EigeneUeberschrift),
                nameof(Anschreibenkontext.EigenerText),
                nameof(Anschreibenkontext.EigeneFaehigkeiten),
                nameof(Anschreibenkontext.EigenerWerdegang),
                nameof(Anschreibenkontext.Sprache));
    }

    /// <summary>
    /// Anschrift, Mail und Telefon gehören auf den Briefbogen, nicht ins Modell
    /// (ADR-0038). Die Allowlist oben reicht nicht: ein Feld <c>Anschrift</c>
    /// anstelle von <c>EigeneUeberschrift</c> würde die Liste nur verschieben.
    /// </summary>
    [Theory]
    [InlineData("anschrift")]
    [InlineData("address")]
    [InlineData("email")]
    [InlineData("telefon")]
    [InlineData("phone")]
    [InlineData("gehalt")]
    public void Der_Kontext_traegt_keine_Anschrift(string wort)
    {
        typeof(Anschreibenkontext).GetProperties()
            .Select(eigenschaft => eigenschaft.Name.ToLowerInvariant())
            .Should().NotContain(name => name.Contains(wort, StringComparison.Ordinal));
    }

    /// <summary>Und keine Zahl über einen Menschen, auch nicht die eigene.</summary>
    /// <remarks>
    /// ADR-0022 an der Stelle, an der er am leichtesten zu übersehen ist:
    /// „ich bin zu 90 % geeignet" wäre eine Zahl über einen Menschen, nur aus
    /// seiner eigenen Feder.
    /// </remarks>
    [Theory]
    [InlineData("score")]
    [InlineData("rank")]
    [InlineData("weight")]
    [InlineData("fit")]
    [InlineData("percent")]
    public void Kein_verbotenes_Wort_am_Entwurf(string wort)
    {
        var namen = typeof(Bewerbungsentwurf).GetProperties()
            .Select(eigenschaft => eigenschaft.Name.ToLowerInvariant())
            .Concat(typeof(Anschreibenkontext).GetProperties()
                .Select(eigenschaft => eigenschaft.Name.ToLowerInvariant()));

        namen.Should().NotContain(name => name.Contains(wort, StringComparison.Ordinal));
    }

    /// <summary>Das Ausgabeformat wird gelesen, nicht geraten.</summary>
    /// <remarks>
    /// Ein Parser, der nur den Idealfall kann, macht aus einer leichten
    /// Abweichung ein leeres Anschreiben — und ein leeres Anschreiben im Stand
    /// „prüfen" ist eine Einladung, es versehentlich freizugeben.
    /// </remarks>
    [Fact]
    public void Das_Ausgabeformat_ueberlebt_Abweichungen()
    {
        Anschreibenformat.Lies("BETREFF: Bewerbung\n---\nSehr geehrte …")
            .Should().Be(("Bewerbung", "Sehr geehrte …"));

        // Ohne Trennstrich.
        Anschreibenformat.Lies("BETREFF: Bewerbung\nSehr geehrte …")
            .Should().Be(("Bewerbung", "Sehr geehrte …"));

        // Mit Markdown-Zaun und Sternchen.
        Anschreibenformat.Lies("```\nBETREFF: **Bewerbung**\n---\nSehr geehrte …\n```")
            .Should().Be(("Bewerbung", "Sehr geehrte …"));

        // Ganz ohne Marke: dann ist alles der Text, und der Betreff bleibt leer
        // — leer und nicht erfunden.
        Anschreibenformat.Lies("Sehr geehrte …")
            .Should().Be((string.Empty, "Sehr geehrte …"));

        // Unvollständige Betreffzeile: den Rohtext zeigen, sonst bleibt der
        // Brief leer, während das Modell den Betreff noch tippt.
        Anschreibenformat.Lies("BETREFF: Bewerbung als")
            .Should().Be((string.Empty, "BETREFF: Bewerbung als"));
    }
}
