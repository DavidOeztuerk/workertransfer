using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Noelias Betriebsoberfläche am laufenden Dienst — die Tür und der Kanarienvogel.</summary>
/// <remarks>
/// <para><strong>Am Draht und nicht an der Klasse.</strong> Zwischen der Prüfung
/// und dem, was ein Mensch liest, liegen der Verbundpunkt, die Tür, die
/// Weichen und das Entkommen in der Seite — vier Stellen, an denen aus einem
/// richtigen Befund eine falsche Seite werden kann. <c>NachweisTests</c> in der
/// Ganzes-Reihe prüft die Prüfungen; diese Reihe prüft den Weg.</para>
///
/// <para><strong>Der Kanarienvogel ist der Kern.</strong> „Kein Wert, kein
/// Schlüssel, kein Name einer Person“ lässt sich nicht durch Hinsehen
/// feststellen — eine Seite, die vierzehn Befunde trägt, liest niemand Zeile
/// für Zeile, und beim fünfzehnten erst recht nicht. Also wird ein Wert in die
/// Konfiguration gelegt, der dort nichts zu suchen hat, und danach gesucht.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class NoeliareiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "nachweis-geheimnis";
    private const string Loeschgeheimnis = "loesch-geheimnis";

    /// <summary>Ein Schlüssel, wie ihn jemand versehentlich hinterlegt.</summary>
    /// <remarks>
    /// Er steht in <c>Draft:Schluessel</c>, also an genau der Stelle, an der ein
    /// echter stünde, und die KI-Naht gilt damit als eingerichtet. Taucht er in
    /// einer Seite oder im Bericht auf, ist das kein Schönheitsfehler, sondern
    /// ein Schlüssel in einem Dokument, das jemand herumreicht.
    /// </remarks>
    private const string Kanarienvogel = "sk-ant-kanarienvogel-0815";

    /// <summary>Ein Name, wie er in keiner Betriebsauskunft stehen darf.</summary>
    private const string Namenskanarie = "Erika Mustermann";

    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("Nachweis:Geheimnis", Geheimnis);

            // Der Kanarienvogel sitzt DA, WO EIN ECHTER SCHLUESSEL SAESSE.
            host.UseSetting("Draft:Schluessel", Kanarienvogel);
            host.UseSetting("Draft:Adresse", "https://api.anthropic.com/v1/messages");

            // Und ein Name, der in einer Konfiguration nichts verloren hat —
            // aber genau so landet er dort, wenn jemand einen Absender
            // eintraegt.
            host.UseSetting("Mail:Absender", $"{Namenskanarie} <erika@example.org>");
            host.UseSetting("environment", "Development");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Noelias Abschnitte — einmal, damit keiner zurückbleibt.</summary>
    /// <remarks>
    /// Bis ADR-0045 standen hier sieben selbstgebaute <c>/nachweis/…</c>-Adressen.
    /// Sie sind gelöscht; <c>Noelia.Dashboard</c> liefert dieselben Fragen unter
    /// <c>/noelia</c>, samt <c>report.json</c> aus derselben Lesung.
    /// </remarks>
    private static readonly string[] Adressen =
    [
        "/noelia", "/noelia/security", "/noelia/sovereignty", "/noelia/ai",
        "/noelia/obligations", "/noelia/composition", "/noelia/report.json"
    ];

    private HttpClient MitGeheimnis()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Nachweis-Secret", Geheimnis);
        return browser;
    }

    // --------------------------------------------------------------- Die Tür

    /// <summary>Ohne Geheimnis: 404, nicht 403.</summary>
    /// <remarks>
    /// <strong>Eine Betriebsoberfläche, deren Existenz man erraten kann, ist
    /// selbst schon eine Auskunft</strong> — ein 403 sagte „hier gibt es etwas“.
    /// </remarks>
    [Theory]
    [InlineData("/noelia")]
    [InlineData("/noelia/security")]
    [InlineData("/noelia/sovereignty")]
    [InlineData("/noelia/ai")]
    [InlineData("/noelia/obligations")]
    [InlineData("/noelia/composition")]
    [InlineData("/noelia/report.json")]
    public async Task Ohne_Geheimnis_antwortet_jede_Adresse_mit_404(string pfad)
    {
        var antwort = await _dienst.CreateClient().GetAsync(pfad);

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
        antwort.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            "ein 403 sagte, dass es hier etwas zu holen gibt");
    }

    /// <summary>Ein falsches Geheimnis sieht aus wie gar keins.</summary>
    /// <remarks>
    /// Bis auf die Korrelationskennung byteweise dieselbe Antwort: niemand soll
    /// „falsch geraten“ von „noch nicht verdrahtet“ unterscheiden können.
    /// </remarks>
    [Fact]
    public async Task Ein_falsches_Geheimnis_antwortet_wie_gar_keins()
    {
        var falsch = _dienst.CreateClient();
        falsch.DefaultRequestHeaders.Add("X-Nachweis-Secret", "daneben");

        var mitFalschem = await falsch.GetAsync("/noelia");
        var ohne = await _dienst.CreateClient().GetAsync("/noelia");

        mitFalschem.StatusCode.Should().Be(HttpStatusCode.NotFound);

        OhneKorrelation(await mitFalschem.Content.ReadAsStringAsync())
            .Should().Be(OhneKorrelation(await ohne.Content.ReadAsStringAsync()));
    }

    /// <summary>Ohne gesetztes Geheimnis ist die Tür zu — auch mit Kopf.</summary>
    /// <remarks>
    /// <para><strong>Dieser Test fehlte, und ein Gegenversuch hat es
    /// gezeigt.</strong> Die Reihe setzte das Geheimnis immer, also lief der
    /// Zweig „nicht gesetzt“ in keinem Test — ein Patch, der bei leerem
    /// Geheimnis <em>durchlässt</em>, blieb grün. Genau das ist gemeint mit:
    /// eine Gegenprobe, die nicht fällt, zeigt einen schwachen Test, nicht
    /// richtigen Code.</para>
    ///
    /// <para><strong>Leer heißt zu, nicht offen.</strong> Eine nicht gesetzte
    /// Variable ist der Zweifelsfall, und bei einer Seite, die sagt, welche
    /// Anbieter diese Instanz benutzt, wäre eine Vorgabe, die im Zweifel
    /// öffnet, die schlechteste.</para>
    /// </remarks>
    [Fact]
    public async Task Ohne_gesetztes_Geheimnis_ist_die_Tuer_zu()
    {
        using var ohneTuer = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Nachweis:Geheimnis", "");
            host.UseSetting("environment", "Development");
        });

        var browser = ohneTuer.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Nachweis-Secret", Geheimnis);

        foreach (var pfad in Adressen)
        {
            (await browser.GetAsync(pfad)).StatusCode
                .Should().Be(
                    HttpStatusCode.NotFound,
                    $"{pfad}: ein leeres Geheimnis heißt zu, nicht offen");
        }

        // Und ohne Kopf genauso — sonst unterschiede sich „nicht eingerichtet"
        // danach, ob jemand etwas vorgezeigt hat.
        (await ohneTuer.CreateClient().GetAsync("/noelia")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ein Pfad, der keinen Abschnitt benennt, antwortet 404.</summary>
    /// <remarks>
    /// Und nicht die Übersicht unter falscher Adresse: eine Seite, die zwei
    /// Dinge heißt, ist eine Regel, an die sich beim nächsten Endpunkt niemand
    /// erinnert.
    /// </remarks>
    [Theory]
    [InlineData("/noelia/erfunden")]
    [InlineData("/nachweis/bericht")]
    [InlineData("/nachweis/ki/mehr")]
    public async Task Ein_Pfad_ohne_Abschnitt_antwortet_404(string pfad) =>
        (await MitGeheimnis().GetAsync(pfad)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

    // ------------------------------------------------------------- Die Seiten

    /// <summary>Mit Geheimnis antwortet jede der sieben Adressen.</summary>
    [Theory]
    [InlineData("/noelia", "text/html")]
    [InlineData("/noelia/security", "text/html")]
    [InlineData("/noelia/sovereignty", "text/html")]
    [InlineData("/noelia/ai", "text/html")]
    [InlineData("/noelia/obligations", "text/html")]
    [InlineData("/noelia/composition", "text/html")]
    [InlineData("/noelia/report.json", "application/json")]
    public async Task Mit_Geheimnis_antwortet_jede_Adresse(string pfad, string art)
    {
        var antwort = await MitGeheimnis().GetAsync(pfad);

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        antwort.Content.Headers.ContentType!.MediaType.Should().Be(art);
    }

    /// <summary>Die KI-Seite trägt die Befunde, die dieser Dienst hat.</summary>
    /// <remarks>
    /// Nicht bloß „sie antwortet": eine leere Seite antwortet auch. Geprüft
    /// wird, dass die drei KI-Kennungen darauf stehen — und dass die
    /// Feldmenge der Naht wirklich aufgeschrieben ist, denn genau das ist der
    /// Grund, warum die Prüfung neben dem Test existiert.
    /// </remarks>
    [Fact]
    public async Task Die_KI_Seite_nennt_die_Feldmenge_der_Naht()
    {
        // Unter `/noelia/security`: Noelias KI-Seite wird von den
        // SOUVERAENITAETS-Abhaengigkeiten gespeist, nicht von Pruefungen.
        var seite = await MitGeheimnis().GetStringAsync("/noelia/security");

        seite.Should().Contain("wt.ki.naht")
            .And.Contain("wt.ki.keine-zahl")
            .And.Contain("wt.ki.anbieter");

        // `wt.ki.protokoll` ist GEFALLEN und steht deshalb nicht mehr da:
        // Noelias `noelia.ai.record-keeping` beantwortet dieselbe Frage. Was
        // Noelia liefert, wird hier geloescht und nicht danebengestellt.
        seite.Should().NotContain("wt.ki.protokoll");
        seite.Should().Contain("noelia.ai.record-keeping");

        // Und das ist der Unterschied, um den es geht: Noelias Verzeichnis
        // erkennt Modell-Endpunkte am HOSTNAMEN und meldet hier „kein
        // erkannter Endpunkt"; unseres liest die eingetragenen Zugaenge und
        // nennt api.anthropic.com. Untergrenze gegen Vollstaendigkeit.
        seite.Should().Contain("noelia.ai.inventory");

        seite.Should().Contain("Ueberschrift").And.Contain("Wunsch");
    }

    /// <summary>
    /// Die Pflichtenseite hat vier Spalten, und keine davon ist ein Urteil.
    /// </summary>
    [Fact]
    public async Task Die_Pflichtenseite_hat_vier_Spalten_und_kein_Urteil()
    {
        var seite = await MitGeheimnis().GetStringAsync("/noelia/obligations");

        seite.Should().Contain("Article")
            .And.Contain("What it asks for")
            .And.Contain("Evidence here")
            .And.Contain("What you still decide");

        foreach (var urteil in new[] { "konform", "zertifi", "compliant" })
        {
            seite.Should().NotContain(
                urteil,
                $"„{urteil}“ wäre eine Urteilsspalte — ein Artikel ist nichts, "
                + "was eine Prüfung bestehen kann");
        }
    }

    /// <summary>Und sie nennt die Frist, wo eine gilt.</summary>
    /// <remarks>
    /// <strong>Ein Dokument, das eine Pflicht von 2027 so darstellt, als binde
    /// sie heute, lädt den Leser ein, zu früh Geld auszugeben.</strong>
    /// </remarks>
    [Fact]
    public async Task Die_Pflichtenseite_nennt_die_Fristen()
    {
        var seite = await MitGeheimnis().GetStringAsync("/noelia/obligations");

        // Anhang III ist UNSER Zitat, nicht Noelias — eine Bibliothek weiss nicht,
        // ob ihr Verbraucher ueber Menschen entscheidet. Dass es hier steht,
        // belegt, dass unsere Pruefungen ihre Zitate mitbringen.
        seite.Should().Contain("Anhang III");

        seite.Should().NotContain(
            "nicht hochriskant",
            "die Einstufung gehoert nicht uns");
    }

    // --------------------------------------------------------- Eine Lesung

    /// <summary>Seite und Bericht sagen dasselbe.</summary>
    /// <remarks>
    /// <strong>Zwei Abfragepfade zu einer Aussage laufen auseinander, und beim
    /// ersten Mal merkt es niemand.</strong> Verglichen werden die Kennungen und
    /// die Stände: dass beide aus <em>einer</em> Lesung kommen, zeigt sich
    /// daran, dass jeder Befund des Berichts auch auf einer Seite steht.
    /// </remarks>
    [Fact]
    public async Task Der_Bericht_und_die_Seiten_kommen_aus_einer_Lesung()
    {
        var browser = MitGeheimnis();

        var bericht = JsonDocument.Parse(
            await browser.GetStringAsync("/noelia/report.json")).RootElement;

        var befunde = bericht.GetProperty("securityChecks").GetProperty("results")
            .EnumerateArray().ToList();

        befunde.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        var seiten = new Dictionary<string, string>
        {
            ["security"] = await browser.GetStringAsync("/noelia/security"),
            ["sovereignty"] = await browser.GetStringAsync("/noelia/sovereignty"),
            ["composition"] = await browser.GetStringAsync("/noelia/composition")
        };

        foreach (var befund in befunde)
        {
            var id = befund.GetProperty("id").GetString()!;

            seiten.Values.Any(seite => seite.Contains(id, StringComparison.Ordinal))
                .Should().BeTrue($"„{id}“ steht im Bericht, aber auf keiner Seite");
        }

        bericht.GetProperty("service").GetString().Should().Be("profile-service");
        bericht.GetProperty("schemaVersion").GetInt32().Should().Be(2);
    }

    /// <summary>Jeder Bezug im Bericht trägt, was offenbleibt.</summary>
    [Fact]
    public async Task Kein_Zitat_im_Bericht_laesst_die_offene_Frage_leer()
    {
        var bericht = JsonDocument.Parse(
            await MitGeheimnis().GetStringAsync("/noelia/report.json")).RootElement;

        var bezuege = bericht.GetProperty("securityChecks").GetProperty("results").EnumerateArray()
            .SelectMany(befund => befund.GetProperty("references").EnumerateArray())
            .ToList();

        bezuege.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        foreach (var bezug in bezuege)
        {
            bezug.GetProperty("reader").GetString()
                .Should().NotBeNullOrWhiteSpace(
                    $"{bezug.GetProperty("citation").GetString()} muss sagen, was "
                    + "ein Mensch danach noch entscheidet");
        }
    }

    // ------------------------------------------------------- Der Kanarienvogel

    /// <summary>
    /// Kein Wert, kein Schlüssel, kein Name einer Person — mit einem
    /// Kanarienvogel geprüft, nicht durch Hinsehen.
    /// </summary>
    /// <remarks>
    /// <para>Der Schlüssel steht in der Konfiguration dieses Dienstes, und die
    /// KI-Naht gilt damit als eingerichtet — der Anbieterbefund <em>spricht</em>
    /// also über ihn. Er nennt <c>api.anthropic.com</c>, und das ist der Zweck;
    /// den Schlüssel nennt er nicht, und das ist die Zusage.</para>
    ///
    /// <para>Geprüft werden alle sieben Adressen auf einmal: eine Zusage, die
    /// nur für sechs davon gilt, ist keine.</para>
    /// </remarks>
    [Fact]
    public async Task Kein_Schluessel_und_kein_Name_steht_in_Seite_oder_Bericht()
    {
        var browser = MitGeheimnis();

        foreach (var pfad in Adressen)
        {
            var inhalt = await browser.GetStringAsync(pfad);

            inhalt.Should().NotContain(
                Kanarienvogel, $"{pfad} trägt einen hinterlegten Schlüssel");

            inhalt.Should().NotContain(
                Namenskanarie, $"{pfad} trägt den Namen eines Menschen");

            inhalt.Should().NotContain(
                postgres.ConnectionString,
                $"{pfad} trägt eine Verbindungszeichenfolge");

            inhalt.Should().NotContain(
                Tokenform.Geheimnis, $"{pfad} trägt das Signaturgeheimnis");
        }
    }

    /// <summary>Und der Kanarienvogel ist wirklich da, wo er hingehört.</summary>
    /// <remarks>
    /// <strong>Die Gegenprobe zum Test darüber.</strong> Ohne sie bliebe offen,
    /// ob der Schlüssel fehlt, weil niemand ihn hinausschreibt — oder weil ihn
    /// nie jemand gesetzt hat. Das Verzeichnis nennt den Host, also spricht der
    /// Befund über genau diesen Anbieter.
    /// </remarks>
    [Fact]
    public async Task Der_Anbieter_steht_da_und_nur_sein_Host()
    {
        // Unter `/noelia/security`: Noelias KI-Seite wird von den
        // SOUVERAENITAETS-Abhaengigkeiten gespeist, nicht von Pruefungen.
        var seite = await MitGeheimnis().GetStringAsync("/noelia/security");

        seite.Should().Contain("api.anthropic.com");
                seite.Should().NotContain("/v1/messages", "die Adresse ist nicht der Host");
    }

    private static string OhneKorrelation(string rumpf) =>
        System.Text.RegularExpressions.Regex.Replace(
            rumpf, "\"correlationId\"\\s*:\\s*\"[^\"]*\"", "\"correlationId\":\"\"");
}
