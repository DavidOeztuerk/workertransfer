using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.ServiceDefaults.Rollen;
using WorkerTransfer.Jobs.Domain.Stellen;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>Die Ortstabelle und die Entfernung, ohne Dienst und ohne Datenbank.</summary>
public class OrtskundeTests
{
    /// <summary>Ein Ortsname wird zu einem Punkt — grosszügig gelesen.</summary>
    /// <remarks>
    /// Was Unternehmen in das Feld schreiben, ist Freitext: „Berlin, Mitte",
    /// „Hamburg / Remote", „Raum Stuttgart", „MÜNCHEN". Eine Tabelle, die nur
    /// die exakte Kleinschreibung trifft, verfehlte die Mehrheit der Anzeigen
    /// und liesse sie stumm aus jeder Umkreissuche fallen.
    /// </remarks>
    [Theory]
    [InlineData("Berlin")]
    [InlineData("berlin")]
    [InlineData("BERLIN")]
    [InlineData("  Berlin  ")]
    [InlineData("Berlin, Mitte")]
    [InlineData("Berlin / Remote")]
    [InlineData("Berlin (hybrid)")]
    [InlineData("Raum Berlin")]
    public void Ein_Ortsname_wird_grosszuegig_gelesen(string geschrieben)
    {
        var punkt = Ortskunde.Finde(geschrieben);

        punkt.Should().NotBeNull();
        punkt!.Value.Breite.Should().BeApproximately(52.52, 0.01);
        punkt.Value.Laenge.Should().BeApproximately(13.405, 0.01);
    }

    /// <summary>Umlaute treffen in beiden Schreibweisen.</summary>
    [Theory]
    [InlineData("München")]
    [InlineData("Muenchen")]
    [InlineData("MÜNCHEN")]
    public void Umlaute_treffen_in_beiden_Schreibweisen(string geschrieben) =>
        Ortskunde.Finde(geschrieben).Should().NotBeNull();

    /// <summary>Der längere Name gewinnt.</summary>
    /// <remarks>
    /// „Frankfurt am Main" enthält „Frankfurt". Gewänne der kürzere Treffer,
    /// entschiede die Aufzählungsreihenfolge eines Dictionary darüber, wo eine
    /// Stelle liegt — und die ist keine Zusage.
    /// </remarks>
    [Fact]
    public void Der_laengere_Name_gewinnt()
    {
        var lang = Ortskunde.Finde("Frankfurt am Main");
        var kurz = Ortskunde.Finde("Frankfurt");

        lang.Should().NotBeNull();
        lang.Should().Be(kurz);
    }

    /// <summary>Die eingebettete Tabelle ist überhaupt da.</summary>
    /// <remarks>
    /// Ohne diese Probe wäre eine fehlende <c>Postleitzahlen.txt</c> von „kein
    /// Ort bekannt" nicht zu unterscheiden — die Umkreissuche fände dann nichts
    /// und beschwerte sich nicht. Die Zahlen sind grob und nach unten
    /// abgesichert, damit ein neuer Datenstand sie nicht rot färbt.
    /// </remarks>
    [Fact]
    public void Die_Tabelle_ist_mitgeliefert()
    {
        var (postleitzahlen, orte) = Ortskunde.Umfang();

        postleitzahlen.Should().BeGreaterThan(10000);
        orte.Should().BeGreaterThan(10000);
    }

    /// <summary>Eine Postleitzahl trifft genauer als ein Name.</summary>
    /// <remarks>
    /// Der Grund, warum die Anzeige ein eigenes Feld dafür hat: „Walldorf"
    /// gibt es mehrfach, „69190" nur einmal.
    /// </remarks>
    [Theory]
    [InlineData("10115", 52.53, 13.38)]
    [InlineData("80331", 48.13, 11.57)]
    [InlineData("69190", 49.30, 8.64)]
    [InlineData("D-10115", 52.53, 13.38)]
    [InlineData(" 10115 ", 52.53, 13.38)]
    public void Eine_Postleitzahl_trifft(string plz, double breite, double laenge)
    {
        var punkt = Ortskunde.Finde(plz, "");

        punkt.Should().NotBeNull();
        punkt!.Value.Breite.Should().BeApproximately(breite, 0.05);
        punkt.Value.Laenge.Should().BeApproximately(laenge, 0.05);
    }

    /// <summary>Eine Grosskunden-Postleitzahl bezeichnet keinen Ort.</summary>
    /// <remarks>
    /// <c>10875</c> hat im Rohdatensatz sechsunddreissig Zeilen mit
    /// Firmennamen, mit Koordinaten von Stuttgart über Berlin bis Bautzen. Ein
    /// Mittelwert daraus läge im Nirgendwo — die Zeile fliegt beim Erzeugen der
    /// Tabelle raus, und die Anzeige gilt als „Ort unbekannt".
    /// </remarks>
    [Fact]
    public void Eine_Grosskunden_Postleitzahl_ist_kein_Ort() =>
        Ortskunde.Finde("10875", "").Should().BeNull();

    /// <summary>
    /// Ein vierstelliger Code gehört Österreich UND der Schweiz — der Ortsname
    /// entscheidet.
    /// </summary>
    /// <remarks>
    /// Ohne Ortsnamen bleibt so ein Code unbekannt. Einen der beiden Kandidaten
    /// zu greifen wäre ein Münzwurf zwischen zwei Ländern, und der Fehlgriff
    /// läge sechshundert Kilometer daneben.
    /// </remarks>
    [Fact]
    public void Ein_mehrdeutiger_Code_braucht_den_Ortsnamen()
    {
        var wien = Ortskunde.Finde("1010", "Wien");
        var ohne = Ortskunde.Finde("1010", "");

        wien.Should().NotBeNull();
        wien!.Value.Breite.Should().BeApproximately(48.21, 0.1);
        ohne.Should().BeNull();
    }

    /// <summary>Eine Postleitzahl im Freitext wird auch gelesen.</summary>
    /// <remarks>
    /// Anzeigen von vor dem eigenen Feld schreiben „10115 Berlin" in die
    /// Ortszeile. Ihnen deswegen den Ort abzusprechen wäre eine Lücke ohne
    /// Grund.
    /// </remarks>
    [Fact]
    public void Eine_Postleitzahl_im_Freitext_wird_gelesen()
    {
        var punkt = Ortskunde.Finde(null, "10115 Berlin");

        punkt.Should().NotBeNull();
        punkt!.Value.Breite.Should().BeApproximately(52.53, 0.05);
    }

    /// <summary>Der grosse Ort gewinnt gegen seine Namensvettern.</summary>
    /// <remarks>
    /// „Husum" gibt es fünfmal in Deutschland. Der Ort in Nordfriesland hat
    /// 20841 Einwohner, der nächste 2336 — wer „Husum" schreibt, meint den
    /// ersten. „Neustadt" dagegen ist wirklich mehrdeutig, und dort ist „weiss
    /// ich nicht" die richtige Antwort: ein Fehlgriff läge Hunderte Kilometer
    /// daneben und fiele niemandem auf.
    /// </remarks>
    [Fact]
    public void Der_grosse_Ort_gewinnt_der_mehrdeutige_nicht()
    {
        var husum = Ortskunde.Finde("Husum");

        husum.Should().NotBeNull();
        husum!.Value.Breite.Should().BeApproximately(54.49, 0.1);

        Ortskunde.Finde("Neustadt").Should().BeNull();
    }

    /// <summary>Fremdsprachige Namensformen grosser Städte treffen auch.</summary>
    /// <remarks>
    /// Die Oberfläche spricht drei Sprachen (ADR-0031), und in einer englischen
    /// Anzeige steht „Munich".
    /// </remarks>
    [Theory]
    [InlineData("Munich", 48.14, 11.57)]
    [InlineData("Cologne", 50.93, 6.95)]
    [InlineData("Vienna", 48.21, 16.37)]
    [InlineData("Geneva", 46.20, 6.15)]
    public void Fremdsprachige_Namen_treffen(string name, double breite, double laenge)
    {
        var punkt = Ortskunde.Finde(name);

        punkt.Should().NotBeNull();
        punkt!.Value.Breite.Should().BeApproximately(breite, 0.1);
        punkt.Value.Laenge.Should().BeApproximately(laenge, 0.1);
    }

    /// <summary>Aus Prosa wird kein Ort herausgepflückt.</summary>
    /// <remarks>
    /// <strong>Gemessen, nicht ausgedacht.</strong> Die Ortstabelle kennt
    /// sechsundzwanzigtausend Namen, und viele davon sind gewöhnliche Wörter:
    /// <em>Hof</em> ist eine Stadt in Bayern mit 46000 Einwohnern, ebenso
    /// <em>Essen</em>, <em>Halle</em>, <em>Lage</em>, <em>Brand</em> und
    /// <em>Bad</em>. Solange jede Wortfolge einer Ortsangabe nachgeschlagen
    /// wurde, lag „Auf dem Hof meiner Oma" in Oberfranken — und niemand hätte
    /// es gemerkt, weil eine Anzeige an einem falschen Ort genauso aussieht wie
    /// eine an einem richtigen.
    /// <para>
    /// Bei zwei bis drei Wörtern bleibt die Zerlegung erlaubt: „Raum Stuttgart"
    /// ist eine Lesart, „Auf dem Hof meiner Oma" ein Ratespiel.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Auf dem Hof meiner Oma")]
    [InlineData("Wir sitzen mitten in der Lage")]
    [InlineData("Zum Essen gehen wir raus")]
    public void Aus_Prosa_wird_kein_Ort_gepflueckt(string satz) =>
        Ortskunde.Finde(satz).Should().BeNull();

    /// <summary>Zwei bis drei Wörter werden weiterhin zerlegt.</summary>
    [Theory]
    [InlineData("Raum Stuttgart")]
    [InlineData("Grossraum Muenchen")]
    [InlineData("Standort: Hamburg")]
    [InlineData("Berlin oder remote")]
    public void Kurze_Angaben_werden_weiterhin_zerlegt(string angabe) =>
        Ortskunde.Finde(angabe).Should().NotBeNull();

    /// <summary>Was die Tabelle nicht kennt, wird nicht geraten.</summary>
    /// <remarks>
    /// <c>null</c> heisst „darüber wissen wir nichts" und nie „liegt bei 0,0" —
    /// ein Nullpunkt im Golf von Guinea machte aus jeder unbekannten Anzeige
    /// eine, die 5000 Kilometer entfernt ist, statt einer, über die niemand
    /// etwas gesagt hat.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Atlantis")]
    [InlineData("Elbonien")]
    public void Unbekanntes_wird_nicht_geraten(string geschrieben) =>
        Ortskunde.Finde(geschrieben).Should().BeNull();

    /// <summary>Die Entfernung stimmt gegen bekannte Strecken.</summary>
    /// <remarks>
    /// Drei Strecken statt einer, und eine davon in Ost-West-Richtung: eine
    /// Formel, die den Längengrad nicht mit dem Kosinus der Breite verkürzt,
    /// stimmt bei Nord-Süd-Strecken und ist bei Ost-West um Faktor 1,6 zu gross
    /// — mit nur einer Probe fiele das nicht auf.
    /// </remarks>
    [Theory]
    [InlineData("Berlin", "Hamburg", 255)]
    [InlineData("Berlin", "Potsdam", 27)]
    [InlineData("Köln", "Dresden", 470)]
    public void Die_Entfernung_stimmt(string von, string nach, double sollKm)
    {
        var a = Ortskunde.Finde(von)!.Value;
        var b = Ortskunde.Finde(nach)!.Value;

        // Zehn Prozent Toleranz: die Zahlen sind Stadtmittelpunkte, und keine
        // Umkreissuche hängt an einem Kilometer.
        Ortskunde.EntfernungKm(a, b).Should().BeApproximately(sollKm, sollKm * 0.1);
    }

    /// <summary>Dieselbe Stadt ist null Kilometer weit weg.</summary>
    [Fact]
    public void Dieselbe_Stadt_ist_null_Kilometer_weit_weg()
    {
        var berlin = Ortskunde.Finde("Berlin")!.Value;

        Ortskunde.EntfernungKm(berlin, berlin).Should().BeApproximately(0, 0.001);
    }

    /// <summary>Unvollständiges ergibt keinen Filter, kein halbes Ergebnis.</summary>
    [Theory]
    [InlineData(null, 13.4, 25.0)]
    [InlineData(52.5, null, 25.0)]
    [InlineData(52.5, 13.4, null)]
    [InlineData(200.0, 13.4, 25.0)]
    [InlineData(52.5, 400.0, 25.0)]
    public void Unvollstaendiges_ergibt_keinen_Umkreis(
        double? breite, double? laenge, double? radius) =>
        Umkreis.Aus(breite, laenge, radius).Should().BeNull();

    /// <summary>Ein unsinniger Radius wird geklemmt, nicht verworfen.</summary>
    /// <remarks>
    /// Wer 100000 schickt, meint „weit". Daraus gar keinen Filter zu machen
    /// wäre die überraschendere Antwort — die Liste zeigte dann still alles.
    /// </remarks>
    [Theory]
    [InlineData(0.5, Umkreis.KleinsterRadiusKm)]
    [InlineData(100000.0, Umkreis.GroessterRadiusKm)]
    [InlineData(-40.0, Umkreis.KleinsterRadiusKm)]
    [InlineData(50.0, 50.0)]
    public void Ein_unsinniger_Radius_wird_geklemmt(double gewuenscht, double soll) =>
        Umkreis.Aus(52.5, 13.4, gewuenscht)!.RadiusKm.Should().Be(soll);
}

/// <summary>Die Umkreissuche über <c>GET /jobs</c>.</summary>
/// <remarks>
/// <strong>Der Punkt kommt aus dem Browser und ist absichtlich ungenau</strong>
/// — zwei Nachkommastellen, gut ein Kilometer. Diese Reihe prüft nicht die
/// Rundung (die passiert im Browser), sondern das, was davon abhängt: dass eine
/// Anzeige, über deren Ort NICHTS bekannt ist, nicht als „weit weg" behandelt,
/// sondern GEZÄHLT und gemeldet wird. Ein Filter, der stumm weglässt, liefert
/// ein Ergebnis, das vollständig aussieht und es nicht ist.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class UmkreissucheTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "rueckzug-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IEntwerfer>(_ => new Probeentwerfer()));
                // Veroeffentlichen verlangt seit PBI-2 einen `admin`. Diese
                // Reihe misst das Blaettern und nicht die Rolle — also
                // antwortet die Probe wie eine Inhaberin.
                dienste.Replace(ServiceDescriptor.Scoped<IFirmenrollen>(_ => new Rollenprobe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    // Berlin, gerundet wie der Browser es schickt.
    private const string Berlin = "lat=52.52&lon=13.41";

    private HttpClient AlsFirma()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Firma(Guid.CreateVersion7(), Guid.CreateVersion7()));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Legt eine veröffentlichte Anzeige an und gibt ihren Titel zurück.</summary>
    /// <remarks>
    /// Das <paramref name="kennwort"/> steht in der Beschreibung und wird bei
    /// jeder Probe als <c>q</c> mitgeschickt. Ohne diese Klammer sähe eine Probe
    /// auch die Anzeigen der übrigen Reihen dieser Sammlung — sie teilen sich
    /// eine Datenbank, und eine Zählung über fremde Zeilen ist keine Aussage.
    /// </remarks>
    private async Task<Guid> LegeAn(
        string ort, string kennwort, string remote = "hybrid", string skill = "C#")
    {
        // EIN Browser für beide Schritte. Zwei Aufrufe von `AlsFirma()` wären
        // zwei verschiedene Unternehmen, und das Veröffentlichen einer fremden
        // Anzeige beantwortet der Dienst mit 403 — die Anzeige bliebe Entwurf,
        // und jede Probe zählte null Treffer, ohne dass etwas kaputt wäre.
        var browser = AlsFirma();

        var angelegt = await browser.PostAsJsonAsync("/jobs", new
        {
            title = $"Stelle {Guid.NewGuid():N}",
            description = $"Wir bauen verteilte Systeme. {kennwort}",
            location = ort,
            remote_mode = remote,
            employment_type = "full_time",
            skills = new[] { skill }
        });

        angelegt.EnsureSuccessStatusCode();

        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        (await browser.PostAsync($"/jobs/{id}/publish", null)).EnsureSuccessStatusCode();

        return id;
    }

    private async Task<JsonElement> Suche(string abfrage) =>
        await Json(await _dienst.CreateClient().GetAsync($"/jobs?{abfrage}"));

    private static IEnumerable<Guid> Kennungen(JsonElement seite) =>
        seite.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());

    /// <summary>Nah ist drin, fern ist draussen — und der Radius entscheidet.</summary>
    /// <remarks>
    /// Potsdam liegt siebenundzwanzig Kilometer von Berlin. Es steht hier statt
    /// Hamburg, weil es GENAU zwischen den beiden geprüften Radien liegt: eine
    /// Suche, die den Radius gar nicht liest, wäre bei Hamburg (255 km) in
    /// beiden Fällen gleich und sähe grün aus.
    /// </remarks>
    [Fact]
    public async Task Der_Radius_entscheidet_wer_drin_ist()
    {
        var kennwort = $"radius{Guid.NewGuid():N}";
        var nah = await LegeAn("Berlin", kennwort);
        var mittel = await LegeAn("Potsdam", kennwort);
        var fern = await LegeAn("Hamburg", kennwort);

        var eng = await Suche($"q={kennwort}&{Berlin}&radius_km=25");
        var weit = await Suche($"q={kennwort}&{Berlin}&radius_km=50");
        var sehrWeit = await Suche($"q={kennwort}&{Berlin}&radius_km=300");

        Kennungen(eng).Should().BeEquivalentTo([nah]);
        Kennungen(weit).Should().BeEquivalentTo([nah, mittel]);
        Kennungen(sehrWeit).Should().BeEquivalentTo([nah, mittel, fern]);
    }

    /// <summary>Voll remote ist immer dabei.</summary>
    /// <remarks>
    /// Von wo aus so eine Stelle erreichbar ist, ist keine Frage der Entfernung.
    /// Sie wegen eines Ortsfilters zu verstecken träfe genau die Anzeigen, die
    /// für jemanden ausserhalb der Ballungsräume die interessantesten sind.
    /// </remarks>
    [Fact]
    public async Task Voll_remote_ist_immer_dabei()
    {
        var kennwort = $"remote{Guid.NewGuid():N}";
        var weitWegAberRemote = await LegeAn("Wien", kennwort, remote: "full");
        await LegeAn("Wien", kennwort, remote: "hybrid");

        var eng = await Suche($"q={kennwort}&{Berlin}&radius_km=25");

        Kennungen(eng).Should().BeEquivalentTo([weitWegAberRemote]);
    }

    /// <summary>Ein unbekannter Ort fällt heraus — und wird GEZÄHLT.</summary>
    /// <remarks>
    /// Der eigentliche Grund dieser Reihe. Eine Umkreissuche kann über eine
    /// Anzeige, deren Ort die Tabelle nicht kennt, nichts sagen. Sie stumm
    /// wegzulassen wäre die Lüge durch Auslassen aus ADR-0022 §3: das Ergebnis
    /// sähe vollständig aus. Die Zahl geht als <c>omitted</c> hinaus, damit die
    /// Oberfläche sie nennen kann.
    /// </remarks>
    [Fact]
    public async Task Ein_unbekannter_Ort_faellt_heraus_und_wird_gezaehlt()
    {
        var kennwort = $"unbekannt{Guid.NewGuid():N}";
        var bekannt = await LegeAn("Berlin", kennwort);
        await LegeAn("Auf dem Hof meiner Oma", kennwort);
        await LegeAn("", kennwort);

        var seite = await Suche($"q={kennwort}&{Berlin}&radius_km=25");

        Kennungen(seite).Should().BeEquivalentTo([bekannt]);
        seite.GetProperty("total_items").GetInt32().Should().Be(1);
        seite.GetProperty("omitted").GetInt32().Should().Be(2);
    }

    /// <summary>Ohne Umkreissuche steht <c>omitted</c> gar nicht in der Antwort.</summary>
    /// <remarks>
    /// Eine <c>0</c> behauptete, die Frage sei gestellt und mit „keine"
    /// beantwortet worden. Wurde sie aber nicht — es gab keinen Ortsfilter, also
    /// hat nichts etwas ausgelassen.
    /// </remarks>
    [Fact]
    public async Task Ohne_Umkreissuche_gibt_es_kein_ausgelassen()
    {
        var kennwort = $"ohne{Guid.NewGuid():N}";
        await LegeAn("Auf dem Hof meiner Oma", kennwort);

        var seite = await Suche($"q={kennwort}");

        seite.GetProperty("total_items").GetInt32().Should().Be(1);
        seite.TryGetProperty("omitted", out _).Should().BeFalse();
    }

    /// <summary>Eine unvollständige Umkreisangabe filtert nicht.</summary>
    /// <remarks>
    /// Ein Radius ohne Punkt ist keine Frage, die man beantworten kann. Sie zu
    /// beantworten, als stünde der Punkt bei 0,0, wäre schlimmer als sie nicht
    /// zu stellen — die Liste wäre leer, und niemand wüsste warum.
    /// </remarks>
    [Theory]
    [InlineData("radius_km=25")]
    [InlineData("lat=52.52&radius_km=25")]
    [InlineData("lat=abc&lon=xyz&radius_km=25")]
    public async Task Eine_unvollstaendige_Umkreisangabe_filtert_nicht(string umkreis)
    {
        var kennwort = $"halb{Guid.NewGuid():N}";
        await LegeAn("Hamburg", kennwort);

        var seite = await Suche($"q={kennwort}&{umkreis}");

        seite.GetProperty("total_items").GetInt32().Should().Be(1);
        seite.TryGetProperty("omitted", out _).Should().BeFalse();
    }

    /// <summary>Umkreis und Fähigkeiten wirken zusammen, nicht gegeneinander.</summary>
    /// <remarks>
    /// Beide Filter laufen im Speicher, und beide zählen die gefilterte Menge.
    /// Liefe einer über das Ergebnis des anderen hinweg, stünde eine Gesamtzahl
    /// über einer Liste, die es so nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Umkreis_und_Faehigkeiten_wirken_zusammen()
    {
        var kennwort = $"beides{Guid.NewGuid():N}";
        var treffer = await LegeAn("Berlin", kennwort, skill: "Rust");
        await LegeAn("Berlin", kennwort, skill: "Cobol");
        await LegeAn("Hamburg", kennwort, skill: "Rust");

        var seite = await Suche($"q={kennwort}&skill=Rust&{Berlin}&radius_km=25");

        Kennungen(seite).Should().BeEquivalentTo([treffer]);
        seite.GetProperty("total_items").GetInt32().Should().Be(1);
        seite.GetProperty("total_pages").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// OHNE Koordinaten ist der Ort die Mitte — und dann kein Textfilter mehr.
    /// </summary>
    /// <remarks>
    /// Wer „Leipzig" tippt und „200 km" wählt, meint „um Leipzig herum" und
    /// braucht dafür weder GPS noch die Erlaubnis dazu.
    /// <para>
    /// Der zweite Teil ist der wichtigere: beides zugleich — Umkreis UND
    /// Wortfilter — schlösse genau die Nachbarorte aus, wegen derer jemand
    /// einen Umkreis wählt. Eine Anzeige in Berlin liegt 150 km von Leipzig und
    /// trägt das Wort „Leipzig" nirgends; sie MUSS dabei sein.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Ohne_Koordinaten_ist_der_Ort_die_Mitte()
    {
        var kennwort = $"mitte{Guid.NewGuid():N}";
        var leipzig = await LegeAn("Leipzig", kennwort);
        var berlin = await LegeAn("Berlin", kennwort);
        await LegeAn("Wien", kennwort);

        var eng = await Suche($"q={kennwort}&location=Leipzig&radius_km=25");
        var weit = await Suche($"q={kennwort}&location=Leipzig&radius_km=200");

        Kennungen(eng).Should().BeEquivalentTo([leipzig]);
        Kennungen(weit).Should().BeEquivalentTo([leipzig, berlin]);
    }

    /// <summary>Ohne Umkreis bleibt der Ort ein Textfilter.</summary>
    /// <remarks>
    /// Die Gegenprobe zur vorigen: der Ort wechselt seine Bedeutung nur, wenn
    /// eine Entfernung danebensteht. Ohne sie sucht er weiter nach dem Wort.
    /// </remarks>
    [Fact]
    public async Task Ohne_Umkreis_bleibt_der_Ort_ein_Textfilter()
    {
        var kennwort = $"textfilter{Guid.NewGuid():N}";
        var leipzig = await LegeAn("Leipzig", kennwort);
        await LegeAn("Berlin", kennwort);

        var seite = await Suche($"q={kennwort}&location=Leipzig");

        Kennungen(seite).Should().BeEquivalentTo([leipzig]);
    }

    /// <summary>Die Gegenrichtung: welcher Ort liegt an diesem Punkt?</summary>
    /// <remarks>
    /// Damit die suchende Person SIEHT, wovon aus gemessen wird — ein Umkreis
    /// um einen unsichtbaren Punkt ist eine Zumutung.
    /// <para>
    /// Der letzte Fall ist der, der einmal falsch war: die Rohdaten führen
    /// unter Grosskunden-Postleitzahlen Firmen als „Ortsnamen", und diese
    /// Antwort lautete für Berlin „Adam Opel GmbH" und für München
    /// „Amtsgericht München". Sie werden beim Erzeugen der Tabelle aussortiert.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("lat=52.52&lon=13.39", "Berlin")]
    [InlineData("lat=53.55&lon=9.99", "Hamburg")]
    [InlineData("lat=48.14&lon=11.57", "München")]
    [InlineData("lat=47.37&lon=8.54", "Zürich")]
    public async Task Die_Gegenrichtung_nennt_den_Ort(string punkt, string soll)
    {
        var antwort = await Json(await _dienst.CreateClient().GetAsync($"/jobs/place?{punkt}"));

        antwort.GetProperty("location").GetString().Should().Be(soll);
    }

    /// <summary>Ausserhalb bleibt sie stumm, statt einen Ort zu erfinden.</summary>
    /// <remarks>
    /// Wer nicht in DE, AT oder CH steht, bekommt <c>null</c>. Den nächsten
    /// deutschen Ort zurückzugeben wäre eine Behauptung über den
    /// Aufenthaltsort, die niemand aufgestellt hat — und das Ortsfeld trüge
    /// dann einen Ort, an dem die Person nie war.
    /// </remarks>
    [Theory]
    [InlineData("lat=10.0&lon=10.0")]
    [InlineData("lat=abc&lon=xyz")]
    [InlineData("")]
    public async Task Ausserhalb_nennt_die_Gegenrichtung_nichts(string punkt)
    {
        var antwort = await Json(await _dienst.CreateClient().GetAsync($"/jobs/place?{punkt}"));

        antwort.GetProperty("location").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Die Umkreissuche blättert wie jede andere.</summary>
    [Fact]
    public async Task Die_Umkreissuche_blaettert()
    {
        var kennwort = $"blaettern{Guid.NewGuid():N}";

        for (var i = 0; i < 5; i++)
        {
            await LegeAn("Berlin", kennwort);
        }

        var erste = await Suche($"q={kennwort}&{Berlin}&radius_km=25&page_size=2");
        var zweite = await Suche($"q={kennwort}&{Berlin}&radius_km=25&page_size=2&page=2");

        erste.GetProperty("total_items").GetInt32().Should().Be(5);
        erste.GetProperty("total_pages").GetInt32().Should().Be(3);
        erste.GetProperty("items").GetArrayLength().Should().Be(2);
        Kennungen(erste).Should().NotIntersectWith(Kennungen(zweite));
    }
}
