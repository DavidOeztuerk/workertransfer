using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Applications.Application.Entwuerfe;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Anschreiben;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Die Regeln aus ADR-0034 — als Prüfungen, nicht als Absichtserklärung.</summary>
public sealed class EntwurfsregelnTests
{
    private static readonly DateTimeOffset Jetzt =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDraft Fertig()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.Nimm_text_an("Bewerbung als Entwickler", "Sehr geehrte …", Jetzt);

        return draft;
    }

    /// <summary>
    /// <strong>Es gibt keinen Weg von „entsteht" nach „gesendet".</strong>
    /// </summary>
    /// <remarks>
    /// Zwischen dem Modell und dem Unternehmen stehen zwei Handlungen eines
    /// Menschen: freigeben und senden. Ein Knopf „freigeben und senden" spart
    /// einen Klick und nimmt der Release ihren Sinn — sie ist der Moment, in
    /// dem jemand sagt „so, und nicht anders".
    /// </remarks>
    [Fact]
    public void Ohne_Freigabe_geht_nichts_hinaus()
    {
        var draft = Fertig();

        var attempt = () => draft.Vermerke_versand(Jetzt);

        attempt.Should().Throw<DraftStepNotAllowed>();
        draft.State.Should().Be(DraftState.Pruefen);
    }

    /// <summary>Und aus „entsteht" erst recht nicht.</summary>
    [Fact]
    public void Aus_dem_Entstehen_heraus_gibt_niemand_frei()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        var freigeben = () => draft.Gib_frei(Jetzt);
        var senden = () => draft.Vermerke_versand(Jetzt);

        freigeben.Should().Throw<DraftStepNotAllowed>();
        senden.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>Eine offene Anmerkung verhindert die Release.</summary>
    [Fact]
    public void Mit_offener_Anmerkung_gibt_niemand_frei()
    {
        var draft = Fertig();

        draft.Merke_an("Der zweite Absatz klingt hohl.", "Sehr geehrte", Jetzt);

        var attempt = () => draft.Gib_frei(Jetzt);

        attempt.Should().Throw<OffeneAnmerkungen>();
        draft.State.Should().Be(DraftState.Ueberarbeiten);
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
        var draft = Fertig();

        var attempt = () => draft.Ueberarbeite("Neu", "Ganz anders", Jetzt);

        attempt.Should().Throw<OffeneAnmerkungen>();
        draft.Text.Should().Be("Sehr geehrte …");
        draft.Fassung.Should().Be(1);
    }

    /// <summary>Die Überarbeitung hakt ab und zählt hoch.</summary>
    /// <remarks>
    /// Die Fassungsnummer ist keine Zierde: wer nicht sieht, dass sich etwas
    /// geändert hat, prüft nicht wirklich.
    /// </remarks>
    [Fact]
    public void Die_Ueberarbeitung_hakt_ab_und_zaehlt_hoch()
    {
        var draft = Fertig();

        draft.Merke_an("Kürzer.", null, Jetzt);
        draft.Ueberarbeite("Bewerbung", "Kurz und gut.", Jetzt);

        draft.State.Should().Be(DraftState.Pruefen);
        draft.Fassung.Should().Be(2);
        draft.HatOffeneAnmerkungen.Should().BeFalse();

        // Die Anmerkung BLEIBT — sie ist der Evidence dafür, warum sich der Text
        // geändert hat. Sie zu löschen brächte die Fassungsgeschichte um ihre
        // Begründung.
        draft.Anmerkungen.Should().HaveCount(1);
        draft.Anmerkungen[0].Erledigt.Should().BeTrue();
    }

    /// <summary>Eine Anmerkung nach der Release nimmt sie zurück.</summary>
    /// <remarks>
    /// Wer nach der Release noch etwas findet, hat es gefunden. Eine Release,
    /// die einen Widerspruch überlebt, ist keine.
    /// </remarks>
    [Fact]
    public void Eine_Anmerkung_nach_der_Freigabe_nimmt_sie_zurueck()
    {
        var draft = Fertig();

        draft.Gib_frei(Jetzt);
        draft.State.Should().Be(DraftState.Freigegeben);

        draft.Merke_an("Doch nicht.", null, Jetzt);

        draft.State.Should().Be(DraftState.Ueberarbeiten);

        var attempt = () => draft.Vermerke_versand(Jetzt);
        attempt.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>Selbst zu ändern nimmt die Release ebenfalls zurück.</summary>
    /// <remarks>
    /// Sonst sagte „freigegeben" nichts über den Text aus, der dann hinausgeht.
    /// </remarks>
    [Fact]
    public void Eigene_Aenderung_nach_der_Freigabe_nimmt_sie_zurueck()
    {
        var draft = Fertig();

        draft.Gib_frei(Jetzt);
        draft.Aendere_selbst("Bewerbung", "Ich habe noch etwas geändert.", Jetzt);

        draft.State.Should().Be(DraftState.Pruefen);
    }

    /// <summary>Ein leeres Anschreiben gibt niemand frei.</summary>
    [Fact]
    public void Ein_leeres_Anschreiben_geht_nicht_durch()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.Nimm_text_an("Betreff", "", Jetzt);

        var attempt = () => draft.Gib_frei(Jetzt);

        attempt.Should().Throw<AnmerkungFehler>();
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
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.Scheitere("Es ist kein Entwurfsanbieter eingerichtet.", Jetzt);
        draft.Aendere_selbst("Bewerbung", "Ich schreibe selbst.", Jetzt);

        draft.State.Should().Be(DraftState.Pruefen);
        draft.Text.Should().Be("Ich schreibe selbst.");
    }

    /// <summary>
    /// Nach einem Fehlschlag darf das Modell denselben Entwurf noch einmal
    /// füllen — sonst verfällt eine gelungene Antwort als 422.
    /// </summary>
    [Fact]
    public void Nach_einem_Fehlschlag_darf_das_Modell_nochmal_schreiben()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.Scheitere("Der Entwurfsanbieter antwortet mit 404.", Jetzt);
        draft.Nimm_text_an("Bewerbung", "Sehr geehrte Damen und Herren.", Jetzt);

        draft.State.Should().Be(DraftState.Pruefen);
        draft.Text.Should().Be("Sehr geehrte Damen und Herren.");
        draft.Fehler.Should().BeEmpty();
    }

    /// <summary>
    /// Ein Anschreiben bleibt kurz: 1500 Tokens haben ein lokales Modell
    /// über die Minute gedrückt, und länger als eine Minute wartet niemand.
    /// </summary>
    [Fact]
    public void Das_Anschreiben_fordert_keine_lange_Antwort()
    {
        HttpCoverLetterWriter.HoechsteToken.Should().BeLessThanOrEqualTo(1500);
        CoverLetterContext.Regeln.Should().Contain("250");
        CoverLetterContext.Regeln.Should().Contain("Muttersprachler");
        CoverLetterContext.Regeln.Should().Contain("Erfinde");
        CoverLetterContext.Regeln.Should().Contain("Signatur");
    }

    [Fact]
    public void Ein_Bruchstueck_bleibt_Entsteht()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.Beginne_schreiben(Jetzt);
        var start = draft.SchreibenBegonnen;

        draft.Nimm_bruchstueck("Bewerbung", "Sehr geehrte", Jetzt.AddSeconds(12));

        draft.State.Should().Be(DraftState.Entsteht);
        draft.Text.Should().Be("Sehr geehrte");
        draft.Fassung.Should().Be(1);
        draft.SchreibenBegonnen.Should().Be(start);
    }

    [Fact]
    public void Das_erste_Schreiben_setzt_den_Start()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.SchreibenBegonnen.Should().BeNull();

        draft.Beginne_schreiben(Jetzt, leeren: false);

        draft.SchreibenBegonnen.Should().Be(Jetzt);
        draft.State.Should().Be(DraftState.Entsteht);
    }

    [Fact]
    public void Nach_einem_Fehlschlag_beginnt_das_Schreiben_wieder()
    {
        var draft = ApplicationDraft.Beginne(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Jetzt);

        draft.Scheitere("Zeitüberschreitung", Jetzt);
        draft.Beginne_schreiben(Jetzt);

        draft.State.Should().Be(DraftState.Entsteht);
        draft.Fehler.Should().BeEmpty();
        draft.Text.Should().BeEmpty();
    }

    /// <summary>Der ganze Weg, mit jedem Sprung als eigener Handlung.</summary>
    [Fact]
    public void Der_ganze_Weg_verlangt_jeden_Schritt_einzeln()
    {
        var draft = Fertig();

        draft.Merke_an("Bitte konkreter.", "Sehr geehrte", Jetzt);
        draft.State.Should().Be(DraftState.Ueberarbeiten);

        draft.Ueberarbeite("Bewerbung", "Konkret.", Jetzt);
        draft.State.Should().Be(DraftState.Pruefen);

        draft.Gib_frei(Jetzt);
        draft.State.Should().Be(DraftState.Freigegeben);

        draft.Vermerke_versand(Jetzt);
        draft.State.Should().Be(DraftState.Gesendet);

        // Danach ist Schluss: ein gesendetes Anschreiben ändert niemand mehr.
        var nachtraeglich = () => draft.Aendere_selbst("x", "y", Jetzt);
        nachtraeglich.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>
    /// <strong>Der Kontext trägt nur eigene Angaben und die Anzeige.</strong>
    /// </summary>
    /// <remarks>
    /// Dieselbe Prüfung, die ADR-0024 für <c>AdvertDraft</c> und den
    /// Profilentwurf vorsieht: die Feldliste ist die Grenze. Wer hier ein Feld
    /// ergänzt, beantwortet zuerst — gehört es der bewerbenden Person, oder
    /// steht es in der öffentlichen Anzeige? Alles andere wäre eine Aussage
    /// über einen Dritten.
    /// </remarks>
    [Fact]
    public void Der_Kontext_traegt_nichts_ueber_Dritte()
    {
        typeof(CoverLetterContext).GetProperties()
            .Select(eigenschaft => eigenschaft.Name)
            .Should().BeEquivalentTo(
                // Die Anzeige — öffentlich lesbar, ohne Anmeldung.
                nameof(CoverLetterContext.StellenTitel),
                nameof(CoverLetterContext.Unternehmen),
                nameof(CoverLetterContext.StellenOrt),
                nameof(CoverLetterContext.StellenBeschreibung),
                nameof(CoverLetterContext.GesuchteFaehigkeiten),
                // Die eigenen Angaben.
                nameof(CoverLetterContext.EigenerName),
                nameof(CoverLetterContext.EigeneUeberschrift),
                nameof(CoverLetterContext.EigenerText),
                nameof(CoverLetterContext.EigeneFaehigkeiten),
                nameof(CoverLetterContext.EigenerWerdegang),
                nameof(CoverLetterContext.Sprache));
    }

    /// <summary>
    /// PostalAddress, Mail und Telefon gehören auf den Briefbogen, nicht ins Modell
    /// (ADR-0038). Die Allowlist oben reicht nicht: ein Feld <c>PostalAddress</c>
    /// anstelle von <c>EigeneUeberschrift</c> würde die Liste nur verschieben.
    /// </summary>
    [Theory]
    [InlineData("anschrift")]
    [InlineData("address")]
    [InlineData("email")]
    [InlineData("telefon")]
    [InlineData("phone")]
    [InlineData("gehalt")]
    public void Der_Kontext_traegt_keine_Anschrift(string word)
    {
        typeof(CoverLetterContext).GetProperties()
            .Select(eigenschaft => eigenschaft.Name.ToLowerInvariant())
            .Should().NotContain(name => name.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Und keine Zahl über einen Menschen, auch nicht die eigene.</summary>
    /// <remarks>
    /// ADR-0022 an der Job, an der er am leichtesten zu übersehen ist:
    /// „ich bin zu 90 % geeignet" wäre eine Zahl über einen Menschen, nur aus
    /// seiner eigenen Feder.
    /// </remarks>
    [Theory]
    [InlineData("score")]
    [InlineData("rank")]
    [InlineData("weight")]
    [InlineData("fit")]
    [InlineData("percent")]
    public void Kein_verbotenes_Wort_am_Entwurf(string word)
    {
        var namen = typeof(ApplicationDraft).GetProperties()
            .Select(eigenschaft => eigenschaft.Name.ToLowerInvariant())
            .Concat(typeof(CoverLetterContext).GetProperties()
                .Select(eigenschaft => eigenschaft.Name.ToLowerInvariant()));

        namen.Should().NotContain(name => name.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Das Ausgabeformat wird gelesen, nicht geraten.</summary>
    /// <remarks>
    /// Ein Parser, der nur den Idealfall kann, macht aus einer leichten
    /// Abweichung ein leeres Anschreiben — und ein leeres Anschreiben im State
    /// „prüfen" ist eine Einladung, es versehentlich freizugeben.
    /// </remarks>
    [Fact]
    public void Das_Ausgabeformat_ueberlebt_Abweichungen()
    {
        CoverLetterFormat.Lies("BETREFF: Bewerbung\n---\nSehr geehrte …")
            .Should().Be(("Bewerbung", "Sehr geehrte …"));

        // Ohne Trennstrich.
        CoverLetterFormat.Lies("BETREFF: Bewerbung\nSehr geehrte …")
            .Should().Be(("Bewerbung", "Sehr geehrte …"));

        // Mit Markdown-Zaun und Sternchen.
        CoverLetterFormat.Lies("```\nBETREFF: **Bewerbung**\n---\nSehr geehrte …\n```")
            .Should().Be(("Bewerbung", "Sehr geehrte …"));

        // Ganz ohne Marke: dann ist alles der Text, und der Betreff bleibt leer
        // — leer und nicht erfunden.
        CoverLetterFormat.Lies("Sehr geehrte …")
            .Should().Be((string.Empty, "Sehr geehrte …"));

        // Unvollständige Betreffzeile: den Rohtext zeigen, sonst bleibt der
        // Brief leer, während das Modell den Betreff noch tippt.
        CoverLetterFormat.Lies("BETREFF: Bewerbung als")
            .Should().Be((string.Empty, "BETREFF: Bewerbung als"));
    }
}
