using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Infrastructure.Persistence;
using WorkerTransfer.Resume.Infrastructure.Texterkennung;

namespace WorkerTransfer.Resume.Tests;

/// <summary>
/// Zählt mit, wie oft wirklich gelesen wurde.
/// </summary>
/// <remarks>
/// <strong>Der Zähler ist der Punkt, nicht die Bequemlichkeit.</strong> Die
/// Zusage lautet „ohne Auslösung wird kein Dokument gelesen", und sie liesse
/// sich auch am leeren Index ablesen — schwächer: ein Lesevorgang, der läuft
/// und sein Ergebnis wegwirft, hat das Zeugnis trotzdem gelesen. Gemessen wird
/// deshalb der <em>Aufruf</em>.
/// <para>
/// Er umhüllt den ECHTEN Erkenner statt ihn zu ersetzen: eine Attrappe, die
/// Eingaben wegwirft, hat in diesem Baum schon dreimal einen echten Fehler
/// verborgen.
/// </para>
/// </remarks>
public sealed class Zaehlerkennung(PdfTexterkennung echter) : ITexterkennung
{
    private int _aufrufe;

    /// <summary>Wie oft <see cref="Lies"/> gerufen wurde.</summary>
    public int Aufrufe => Volatile.Read(ref _aufrufe);

    /// <inheritdoc />
    public Erkanntes Lies(ReadOnlyMemory<byte> inhalt, string inhaltstyp)
    {
        Interlocked.Increment(ref _aufrufe);
        return echter.Lies(inhalt, inhaltstyp);
    }
}

/// <summary>
/// Der Elektroniker lädt sein Zeugnis hoch, drückt einen Knopf, und die
/// Plattform sagt ihm, was darin steht (PBI-7, ADR-0043).
/// </summary>
/// <remarks>
/// <para><strong>Mit echten PDFs und dem echten Erkenner.</strong> Die halbe
/// Zusage betrifft die Datei und nicht die Zeile: dass aus einem Bild nichts zu
/// lesen ist, dass die Schreibweise im Zeugnis zum Namen im Profil wird, dass
/// ein Umbruch mitten im Wort nichts kaputtmacht. Eine Erkenner-Attrappe
/// bestätigte all das, ohne eines davon zu prüfen.</para>
///
/// <para>Die PDFs entstehen im Test mit <c>PdfDocumentBuilder</c> — dieselbe
/// Bibliothek, andere Richtung. Das ist kein Zirkelschluss, sondern die einzige
/// Möglichkeit, ohne eine eingecheckte Binärdatei zu prüfen, was ein gesetztes
/// Dokument hergibt.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ErkennungsreiseTests(Postgres postgres) : IAsyncLifetime
{
    /// <summary>Ein echtes PNG — ein Bild, und darin steht kein Text.</summary>
    private static readonly byte[] EinBild =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string _ablage = Path.Combine(
        Path.GetTempPath(), "wt-erkennung-" + Guid.CreateVersion7().ToString("N"));

    private readonly Probetor _tor = new();
    private readonly Probezustellung _versand = new();
    private Zaehlerkennung _zaehler = null!;
    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_ablage);

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:resume", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("Ablage:Verzeichnis", _ablage);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor));
                dienste.Replace(ServiceDescriptor.Scoped<IZustellung>(_ => _versand));
                dienste.Replace(ServiceDescriptor.Singleton<ITexterkennung>(anbieter =>
                    new Zaehlerkennung(
                        new PdfTexterkennung(
                            anbieter.GetRequiredService<
                                Microsoft.Extensions.Logging.ILogger<PdfTexterkennung>>()))));
            });
        });

        // Den Behälter bauen und den Zähler HOLEN, statt ihn beim Registrieren
        // nebenbei zuzuweisen: die Fabrik liefe sonst erst beim ersten Aufruf,
        // und genau der Test, der ZUERST misst, hätte kein `_zaehler`.
        _ = _dienst.CreateClient();
        _zaehler = (Zaehlerkennung)_dienst.Services.GetRequiredService<ITexterkennung>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();

        if (Directory.Exists(_ablage))
        {
            Directory.Delete(_ablage, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>Ein gesetztes PDF mit diesen Zeilen darin.</summary>
    /// <remarks>
    /// Jede Zeile wird einzeln gezeichnet, genau wie in einem echten Dokument —
    /// und damit auch der Fall, an dem PdfPigs <c>Text</c> zwei Blöcke ohne
    /// Leerzeichen aneinandersetzt.
    /// </remarks>
    private static byte[] EinPdfMit(params string[] zeilen)
    {
        var bauer = new PdfDocumentBuilder();
        var seite = bauer.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var schrift = bauer.AddStandard14Font(Standard14Font.Helvetica);

        var y = 760.0;

        foreach (var zeile in zeilen)
        {
            seite.AddText(zeile, 12, new PdfPoint(40, y), schrift);
            y -= 20;
        }

        return bauer.Build();
    }

    private HttpClient AlsPerson(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(wer));

        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Lade(
        HttpClient browser, byte[] inhalt, string dateiname, string name)
    {
        var formular = new MultipartFormDataContent();
        var datei = new ByteArrayContent(inhalt);
        datei.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        formular.Add(datei, "file", dateiname);
        formular.Add(new StringContent(name), "name");
        formular.Add(new StringContent("zeugnis"), "kind");

        return browser.PostAsync("/resumes/me/documents", formular);
    }

    /// <summary>Die Wörter, die zu einer Unterlage gefunden wurden.</summary>
    private static string[] Begriffe(JsonElement stand, string name) =>
        [.. stand.EnumerateArray()
            .First(eintrag => eintrag.GetProperty("name").GetString() == name)
            .GetProperty("terms").EnumerateArray()
            .Select(wort => wort.GetString()!)];

    private async Task<int> FundzeilenFuer(Guid wer)
    {
        await using var quelle = ResumeDbContextFactory.DataSource(postgres.ConnectionString);
        await using var kontext = new ResumeDbContext(
            (DbContextOptions<ResumeDbContext>)ResumeDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<ResumeDbContext>(), quelle).Options);

        return await kontext.Funde.CountAsync(zeile => zeile.SubjectId == wer);
    }

    // -----------------------------------------------------------------------
    // OHNE AUSLÖSUNG WIRD NICHTS GELESEN (ADR-0004)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <strong>Hochladen liest nicht.</strong>
    /// </summary>
    /// <remarks>
    /// Die Zusage, um derentwillen es hier überhaupt einen Knopf gibt. „Auf
    /// Auslösung" allein wäre zahnlos — ein Lesevorgang beim Hochladen wäre
    /// formal auch ausgelöst, nämlich durch das Hochladen. Gemeint ist, was
    /// ADR-0004 für GitHub schon sagt: einmal auf Bitte hinsehen ist etwas
    /// anderes als dauerhaft hinterhersehen.
    /// <para>
    /// Gemessen an drei Stellen, weil jede für sich zu schwach wäre: der
    /// <em>Zähler</em> des Erkenners (es wurde nicht gelesen), die
    /// <em>Antwort</em> (<c>read_at</c> ist null, also „noch nie gelesen") und
    /// die <em>Tabelle</em> (es steht keine Zeile darin).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Ohne_Ausloesung_wird_kein_Dokument_gelesen()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        await Lade(ihr, EinPdfMit("Herr Meier ist Schweissfachmann DVS."), "z.pdf", "Zeugnis");

        _zaehler.Aufrufe.Should().Be(0, "das Hochladen ruft den Erkenner nicht");

        var stand = await Json(await ihr.GetAsync("/resumes/me/documents/terms"));

        stand.GetArrayLength().Should().Be(1);
        stand[0].GetProperty("read_at").ValueKind.Should().Be(JsonValueKind.Null);
        stand[0].GetProperty("terms").GetArrayLength().Should().Be(0);

        // Und auch die Abfrage selbst liest nicht — sonst wäre das Öffnen der
        // Profilseite genau der Hintergrundlauf, den der Knopf vermeidet.
        _zaehler.Aufrufe.Should().Be(0);
        (await FundzeilenFuer(anna)).Should().Be(0);
    }

    /// <summary>
    /// <strong>Ein Klick, und im Zeugnis steht „Schweißfachmann".</strong>
    /// </summary>
    /// <remarks>
    /// Die Abnahme aus PBI-7, ganz: hochgeladen wird ein PDF mit der
    /// Schreibweise ohne Eszett, zurück kommt der kanonische Name. Das ist der
    /// Wortschatz bei der Arbeit — er <em>benennt um</em> und folgert nichts
    /// (ADR-0023): dass jemand auch schweissen kann, steht hier nicht.
    /// </remarks>
    [Fact]
    public async Task Ein_Klick_findet_die_Woerter_im_Zeugnis()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        await Lade(
            ihr,
            EinPdfMit(
                "Zeugnis der Metallbau Nord GmbH",
                "Herr Meier ist Schweissfachmann DVS",
                "und bedient CNC-Maschinen im Tagesbetrieb."),
            "zeugnis.pdf",
            "Arbeitszeugnis Nord");

        var gelesen = await ihr.PostAsync("/resumes/me/documents/read", null);

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);

        var stand = await Json(gelesen);

        stand[0].GetProperty("read_at").ValueKind.Should().NotBe(JsonValueKind.Null);
        stand[0].GetProperty("has_text").GetBoolean().Should().BeTrue();
        Begriffe(stand, "Arbeitszeugnis Nord").Should().Contain("Schweißfachmann").And.Contain("CNC");

        _zaehler.Aufrufe.Should().Be(1, "genau eine Unterlage, genau ein Lesevorgang");
    }

    /// <summary>
    /// <strong>Aus einem Fund folgt kein zweites Wort.</strong>
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe zur Verlockung. Wer „Schweißfachmann" liest, weiss, dass
    /// dieser Mensch schweisst — und darf es trotzdem nicht hinschreiben: das
    /// wäre eine Aussage über ihn, die er nie getroffen hat, an einer Stelle,
    /// an der er nicht widerspricht. Dieselbe Prüfung wie
    /// <c>Aus_MIG_MAG_folgt_kein_Schweissen</c>, eine Schicht höher.
    /// </remarks>
    [Fact]
    public async Task Aus_dem_Fund_wird_nichts_gefolgert()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        await Lade(ihr, EinPdfMit("Schweissfachmann DVS, mit Urkunde."), "z.pdf", "Zeugnis");
        var stand = await Json(await ihr.PostAsync("/resumes/me/documents/read", null));

        Begriffe(stand, "Zeugnis").Should().Equal("Schweißfachmann");
    }

    /// <summary>
    /// <strong>„Kein Text zu lesen" ist etwas anderes als „nichts gefunden".</strong>
    /// </summary>
    /// <remarks>
    /// ADR-0022 §3, wörtlich: wer nichts auf GitHub hat, ist nicht schlechter,
    /// sondern woanders — und eine Ansicht, die das nicht sagt, lügt durch
    /// Auslassung. Ein abfotografierter Meisterbrief ist ein Bild. Beides als
    /// leere Wortliste zu zeigen hiesse, seinem Besitzer stillschweigend
    /// mitzuteilen, darin stehe nichts.
    /// </remarks>
    [Fact]
    public async Task Ein_Bild_sagt_dass_kein_Text_zu_lesen_war()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        await Lade(ihr, EinBild, "brief.png", "Meisterbrief");
        await Lade(ihr, EinPdfMit("Ein Zeugnis ohne bekannte Woerter."), "z.pdf", "Zeugnis");

        var stand = await Json(await ihr.PostAsync("/resumes/me/documents/read", null));

        var bild = stand.EnumerateArray()
            .First(eintrag => eintrag.GetProperty("name").GetString() == "Meisterbrief");
        var text = stand.EnumerateArray()
            .First(eintrag => eintrag.GetProperty("name").GetString() == "Zeugnis");

        // Beide haben keine Wörter …
        bild.GetProperty("terms").GetArrayLength().Should().Be(0);
        text.GetProperty("terms").GetArrayLength().Should().Be(0);

        // … und sie unterscheiden sich trotzdem. Genau das ist der Punkt.
        bild.GetProperty("has_text").GetBoolean().Should().BeFalse();
        text.GetProperty("has_text").GetBoolean().Should().BeTrue();

        // Und beide sind GELESEN — „noch nie gelesen" ist der dritte Zustand.
        bild.GetProperty("read_at").ValueKind.Should().NotBe(JsonValueKind.Null);
        text.GetProperty("read_at").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>Zweimal lesen legt keine zweite Zeile an.</summary>
    /// <remarks>
    /// Der Knopf liest jedes Mal alles neu — absichtlich, denn der Wortschatz
    /// wächst per Pull Request, und ein Zeugnis, das vorher gelesen wurde,
    /// trüge seinen Fund sonst für immer unvollständig. Er ersetzt dabei, statt
    /// anzuhäufen: sonst stünde derselbe Vorschlag doppelt in der Liste.
    /// </remarks>
    [Fact]
    public async Task Zweimal_lesen_ersetzt_statt_anzuhaeufen()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        await Lade(ihr, EinPdfMit("Kenntnisse in CNC und SPS."), "z.pdf", "Zeugnis");

        await ihr.PostAsync("/resumes/me/documents/read", null);
        var zweites = await Json(await ihr.PostAsync("/resumes/me/documents/read", null));

        zweites.GetArrayLength().Should().Be(1);
        Begriffe(zweites, "Zeugnis").Should().Equal("CNC", "SPS");
        (await FundzeilenFuer(anna)).Should().Be(1);
        _zaehler.Aufrufe.Should().Be(2);
    }

    /// <summary>Wer die Unterlage wegnimmt, nimmt den Fund mit.</summary>
    /// <remarks>
    /// Bliebe er stehen, böte die Profilseite weiter Wörter aus einem Zeugnis
    /// an, das die Person eben gelöscht hat — ein Beleg ohne seinen Gegenstand.
    /// </remarks>
    [Fact]
    public async Task Mit_der_Unterlage_faellt_ihr_Fund()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        var angelegt = await Lade(ihr, EinPdfMit("Kenntnisse in CNC."), "z.pdf", "Zeugnis");
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        await ihr.PostAsync("/resumes/me/documents/read", null);
        (await FundzeilenFuer(anna)).Should().Be(1);

        (await ihr.DeleteAsync($"/resumes/me/documents/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await FundzeilenFuer(anna)).Should().Be(0);
        (await Json(await ihr.GetAsync("/resumes/me/documents/terms"))).GetArrayLength().Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // DER INDEX FÄLLT MIT DER LÖSCHUNG (ADR-0027)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <strong>Die Löschung nimmt den Index mit.</strong>
    /// </summary>
    /// <remarks>
    /// Die Auflage, unter der dieser Index überhaupt gebaut werden durfte — und
    /// die Stelle, an der sie am leisesten bricht: nach einer Löschung sind
    /// auch die Unterlagen weg, also antwortet <c>/terms</c> ohnehin leer. Ein
    /// Test, der nur die Antwort ansieht, wäre grün, während die Tabelle die
    /// Wörter aus den Zeugnissen eines gelöschten Menschen weiter hielte.
    /// Gezählt wird deshalb in der DATENBANK.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_der_Person_nimmt_den_Index_mit()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        await Lade(ihr, EinPdfMit("Schweissfachmann DVS, mit Urkunde."), "a.pdf", "Eins");
        await Lade(ihr, EinPdfMit("Kenntnisse in CNC und SPS."), "b.pdf", "Zwei");
        await ihr.PostAsync("/resumes/me/documents/read", null);

        (await FundzeilenFuer(anna)).Should().Be(2);

        var loeschen = _dienst.CreateClient();
        loeschen.DefaultRequestHeaders.Add("X-Erasure-Secret", "loesch-geheimnis");

        (await loeschen.PostAsJsonAsync("/erasure", new { user_id = anna })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await FundzeilenFuer(anna)).Should().Be(0);
    }

    /// <summary>Ein Konto ohne Unterlagen bekommt eine leere Antwort, keinen Fehler.</summary>
    /// <remarks>
    /// Die Zusage „nichts wird schlechter": wer nie etwas hochgeladen hat, sieht
    /// dieselbe Seite wie vorher. Auch der Knopf tut dann nichts — und sagt das,
    /// statt zu scheitern.
    /// </remarks>
    [Fact]
    public async Task Ohne_Unterlagen_ist_die_Antwort_leer()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        (await Json(await ihr.GetAsync("/resumes/me/documents/terms")))
            .GetArrayLength().Should().Be(0);

        var gelesen = await ihr.PostAsync("/resumes/me/documents/read", null);

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(gelesen)).GetArrayLength().Should().Be(0);
        _zaehler.Aufrufe.Should().Be(0);
    }

    /// <summary>Ohne Token beantwortet keine der beiden Adressen etwas.</summary>
    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_nichts()
    {
        var fremder = _dienst.CreateClient();

        (await fremder.GetAsync("/resumes/me/documents/terms")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await fremder.PostAsync("/resumes/me/documents/read", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Gelesen wird nur, was mir gehört.</summary>
    /// <remarks>
    /// Der Knopf nimmt keine Kennung entgegen; wessen Unterlagen gelesen werden,
    /// kommt aus dem geprüften Token. Eine Kennung im Pfad wäre die Einladung,
    /// sie zu ändern.
    /// </remarks>
    [Fact]
    public async Task Der_Knopf_liest_nur_die_eigenen_Unterlagen()
    {
        var anna = Guid.CreateVersion7();
        var bodo = Guid.CreateVersion7();

        await Lade(AlsPerson(anna), EinPdfMit("Kenntnisse in CNC."), "z.pdf", "Annas Zeugnis");

        var seines = await Json(await AlsPerson(bodo).PostAsync("/resumes/me/documents/read", null));

        seines.GetArrayLength().Should().Be(0);
        _zaehler.Aufrufe.Should().Be(0);
        (await FundzeilenFuer(anna)).Should().Be(0);
    }
}
