using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Tests;

/// <summary>Unterlagen: hochladen, zeigen, freigeben, löschen — und die Datei dazu.</summary>
/// <remarks>
/// <para><strong>Mit einer echten Ablage auf der Platte, nicht mit einer
/// Probe.</strong> Die Hälfte der Zusagen aus ADR-0035 betrifft die
/// <em>Datei</em> und nicht die Zeile: dass eine gefälschte Endung nicht hilft,
/// dass ein Löschen auch die Bytes räumt, dass eine Zeile ohne Datei von aussen
/// aussieht wie „gibt es nicht". Eine Ablage-Probe im Speicher würde alle drei
/// bestätigen, ohne eine davon zu prüfen.</para>
///
/// <para>Das Verzeichnis ist je Reihe eigen und wird danach abgeräumt — zwei
/// Reihen, die sich eine Ablage teilen, sind zwei Reihen, deren Reihenfolge
/// zählt.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class UnterlagenreiseTests(Postgres postgres) : IAsyncLifetime
{
    /// <summary>Ein echtes PNG — acht Bytes Signatur genügen der Erkennung.</summary>
    private static readonly byte[] EinPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>Ein echtes PDF.</summary>
    private static readonly byte[] EinPdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    /// <summary>Der Anfang einer Windows-Programmdatei. Kein Bild, wie sie auch heisst.</summary>
    private static readonly byte[] EinProgramm = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

    private readonly string _ablage = Path.Combine(
        Path.GetTempPath(), "wt-unterlagen-" + Guid.CreateVersion7().ToString("N"));

    private readonly Probetor _tor = new();
    private readonly Probezustellung _versand = new();
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
            });
        });

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

    private HttpClient AlsPerson(Guid wer) => Mit(Tokenform.Person(wer));

    private HttpClient AlsFirma(Guid wer, Guid firma) => Mit(Tokenform.Firma(wer, firma));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Lädt hoch, wie ein Browser es täte: multipart, mit Namen und Art.</summary>
    private static Task<HttpResponseMessage> Lade(
        HttpClient browser, byte[] inhalt, string dateiname, string name, string art = "zeugnis")
    {
        var formular = new MultipartFormDataContent();
        var datei = new ByteArrayContent(inhalt);

        // Der BEHAUPTETE Typ, absichtlich: der Dienst darf ihm nicht glauben.
        datei.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        formular.Add(datei, "file", dateiname);
        formular.Add(new StringContent(name), "name");
        formular.Add(new StringContent(art), "kind");

        return browser.PostAsync("/resumes/me/documents", formular);
    }

    /// <summary>Wie viele Dateien wirklich auf der Platte liegen.</summary>
    private int DateienAufDerPlatte() =>
        Directory.Exists(_ablage)
            ? Directory.GetFiles(_ablage, "*", SearchOption.AllDirectories).Length
            : 0;

    /// <summary>Der ganze Weg — und die Datei liegt dabei wirklich da.</summary>
    [Fact]
    public async Task Hochladen_zeigen_holen_loeschen()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var angelegt = await Lade(ihr, EinPdf, "zeugnis.pdf", "Arbeitszeugnis Acme");

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);

        var eintrag = await Json(angelegt);
        var id = eintrag.GetProperty("id").GetGuid();

        eintrag.GetProperty("name").GetString().Should().Be("Arbeitszeugnis Acme");
        eintrag.GetProperty("kind").GetString().Should().Be("zeugnis");
        // Der Typ kommt aus den BYTES: hochgeladen als „image/png" behauptet.
        eintrag.GetProperty("content_type").GetString().Should().Be("application/pdf");
        eintrag.GetProperty("size_bytes").GetInt32().Should().Be(EinPdf.Length);

        DateienAufDerPlatte().Should().Be(1, "die Bytes liegen in der Ablage, nicht in der Zeile");

        var liste = await Json(await ihr.GetAsync("/resumes/me/documents"));
        liste.GetArrayLength().Should().Be(1);

        var inhalt = await ihr.GetAsync($"/resumes/me/documents/{id}/content");
        inhalt.StatusCode.Should().Be(HttpStatusCode.OK);
        inhalt.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await inhalt.Content.ReadAsByteArrayAsync()).Should().Equal(EinPdf);

        (await ihr.DeleteAsync($"/resumes/me/documents/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // ZEILE UND DATEI. Zeilen zu löschen und Bytes liegen zu lassen wäre
        // eine gebrochene Zusage, die niemand bemerkt: die Oberfläche sieht
        // danach leer aus, die Platte nicht (ADR-0027, ADR-0035).
        (await Json(await ihr.GetAsync("/resumes/me/documents"))).GetArrayLength().Should().Be(0);
        DateienAufDerPlatte().Should().Be(0);
    }

    /// <summary>
    /// <strong>Eine gefälschte Endung hilft nicht.</strong>
    /// </summary>
    /// <remarks>
    /// Der Dateiname und der <c>Content-Type</c> sind beide frei wählbar; die
    /// Signatur nicht. Wer der Behauptung glaubt, nimmt eine ausführbare Datei
    /// entgegen, weil jemand sie <c>zeugnis.png</c> genannt hat.
    /// </remarks>
    [Fact]
    public async Task Eine_gefaelschte_Endung_hilft_nicht()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var abgelehnt = await Lade(ihr, EinProgramm, "zeugnis.png", "Sieht aus wie ein Bild");

        abgelehnt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Und es bleibt auch nichts liegen: erst die Bytes, dann die Zeile —
        // wird abgelehnt, wird beides nicht geschrieben.
        DateienAufDerPlatte().Should().Be(0);
        (await Json(await ihr.GetAsync("/resumes/me/documents"))).GetArrayLength().Should().Be(0);
    }

    /// <summary>Und andersherum: ein echtes PNG mit falscher Endung geht durch.</summary>
    /// <remarks>
    /// Die Gegenhälfte. Ohne sie hiesse der Befund oben womöglich „es geht gar
    /// nichts durch".
    /// </remarks>
    [Fact]
    public async Task Ein_echtes_Bild_mit_falscher_Endung_geht_durch()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var angelegt = await Lade(ihr, EinPng, "zeugnis.txt", "Doch ein Bild");

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(angelegt)).GetProperty("content_type").GetString().Should().Be("image/png");
    }

    /// <summary>Zehn sind genug.</summary>
    /// <remarks>
    /// Ohne Grenze baut ein Aufrufer mit einer Schleife eine beliebig teure
    /// Ablage — und eine Bewerbung mit dreissig Anhängen liest ohnehin niemand.
    /// </remarks>
    [Fact]
    public async Task Mehr_als_zehn_nimmt_niemand_an()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        for (var nummer = 1; nummer <= 10; nummer++)
        {
            (await Lade(ihr, EinPdf, $"z{nummer}.pdf", $"Zeugnis {nummer}")).StatusCode
                .Should().Be(HttpStatusCode.Created);
        }

        (await Lade(ihr, EinPdf, "z11.pdf", "Eins zu viel")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);

        DateienAufDerPlatte().Should().Be(10);
    }

    /// <summary>Ein Fremder sieht sie nicht — und merkt auch nicht, dass es sie gibt.</summary>
    [Fact]
    public async Task Ein_Fremder_sieht_die_eigenen_Unterlagen_nicht()
    {
        var anna = Guid.CreateVersion7();
        await Lade(AlsPerson(anna), EinPdf, "z.pdf", "Annas Zeugnis");

        var fremder = AlsPerson(Guid.CreateVersion7());

        // `/me` ist je Aufrufer: der Fremde bekommt SEINE Liste, nicht Annas.
        (await Json(await fremder.GetAsync("/resumes/me/documents")))
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// <strong>Die Firma sieht nichts, solange nichts freigegeben ist.</strong>
    /// </summary>
    /// <remarks>
    /// Und die leere Liste ist dieselbe, die jemand ohne Unterlagen bekommt.
    /// Ein Unterschied verriete, dass diese Person welche <em>hat</em>, und das
    /// ist eine Tatsache über sie (ADR-0020).
    /// </remarks>
    [Fact]
    public async Task Ohne_Freigabe_sieht_die_Firma_nichts()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();

        var angelegt = await Json(await Lade(AlsPerson(anna), EinPdf, "z.pdf", "Zeugnis"));
        var id = angelegt.GetProperty("id").GetGuid();

        var chefin = AlsFirma(Guid.CreateVersion7(), firma);

        (await Json(await chefin.GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(0);

        (await chefin.GetAsync($"/resumes/{anna}/documents/{id}/content")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // Jemand ohne Unterlagen sieht von aussen genauso aus.
        (await Json(await chefin.GetAsync($"/resumes/{Guid.CreateVersion7()}/documents")))
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>Mit Freigabe sieht sie sie — und der Ledger wird jedes Mal gefragt.</summary>
    /// <remarks>
    /// Ein Widerruf muss auf den nächsten Aufruf wirken, nicht auf den
    /// übernächsten (ADR-0013). Deshalb prüft dieser Test beide Richtungen in
    /// einem Zug: erteilen, lesen, widerrufen, wieder lesen.
    /// </remarks>
    [Fact]
    public async Task Mit_Freigabe_sieht_die_Firma_sie_und_ein_Widerruf_wirkt_sofort()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();

        var angelegt = await Json(await Lade(AlsPerson(anna), EinPdf, "z.pdf", "Zeugnis"));
        var id = angelegt.GetProperty("id").GetGuid();

        _tor.UnterlagenFrei.Add((anna, firma));

        var chefin = AlsFirma(Guid.CreateVersion7(), firma);

        (await Json(await chefin.GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(1);

        var inhalt = await chefin.GetAsync($"/resumes/{anna}/documents/{id}/content");
        inhalt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await inhalt.Content.ReadAsByteArrayAsync()).Should().Equal(EinPdf);

        _tor.UnterlagenFrei.Remove((anna, firma));

        (await Json(await chefin.GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(0);
        (await chefin.GetAsync($"/resumes/{anna}/documents/{id}/content")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// <strong>Die Freigabe des Lebenslaufs ist NICHT die der Unterlagen.</strong>
    /// </summary>
    /// <remarks>
    /// Zwei Fähigkeiten, zwei Entscheidungen: ein Werdegang ist selbst
    /// geschriebener Text, ein Zeugnis ein Dokument Dritter mit Namen und Noten
    /// (ADR-0035). Wer das eine zeigen will und das andere nicht, muss das
    /// können — und wenn eine Freigabe beide öffnete, könnte er es nicht.
    /// </remarks>
    [Fact]
    public async Task Die_Lebenslauffreigabe_oeffnet_die_Unterlagen_nicht()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();

        await Lade(AlsPerson(anna), EinPdf, "z.pdf", "Zeugnis");

        // NUR der Lebenslauf ist frei.
        _tor.LebenslaufFrei.Add((anna, firma));

        (await Json(await AlsFirma(Guid.CreateVersion7(), firma)
                .GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>Eine Privatperson liest keine fremden Unterlagen.</summary>
    /// <remarks>
    /// 403 und nicht 404: „kein aktives Unternehmen" ist eine Aussage über den
    /// <em>Aufrufer</em> und verrät nichts über den anderen Menschen.
    /// </remarks>
    [Fact]
    public async Task Eine_Privatperson_liest_keine_fremden_Unterlagen()
    {
        var anna = Guid.CreateVersion7();
        await Lade(AlsPerson(anna), EinPdf, "z.pdf", "Zeugnis");

        (await AlsPerson(Guid.CreateVersion7()).GetAsync($"/resumes/{anna}/documents"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Eine hochgeladene Datei kann DER Lebenslauf sein.</summary>
    /// <remarks>
    /// Der zweite Weg zum Lebenslauf: wer ihn als PDF hat, soll ihn nicht
    /// abtippen müssen. Die Art wechselt, die Datei bleibt dieselbe.
    /// </remarks>
    [Fact]
    public async Task Eine_Datei_wird_zum_Lebenslauf()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        var id = (await Json(await Lade(ihr, EinPdf, "lauf.pdf", "Mein Lebenslauf", "sonstiges")))
            .GetProperty("id").GetGuid();

        (await ihr.PutAsync($"/resumes/me/documents/{id}/as-cv", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var liste = await Json(await ihr.GetAsync("/resumes/me/documents"));
        liste[0].GetProperty("kind").GetString().Should().Be("lebenslauf");
    }

    /// <summary>Was es nicht gibt, wird nicht zum Lebenslauf.</summary>
    [Fact]
    public async Task Eine_fremde_Kennung_wird_nicht_zum_Lebenslauf()
    {
        (await AlsPerson(Guid.CreateVersion7())
                .PutAsync($"/resumes/me/documents/{Guid.CreateVersion7()}/as-cv", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Die Vorlage ist ein Wert, und sie kommt zurück.</summary>
    [Fact]
    public async Task Eine_Vorlage_wird_gesetzt_und_gelesen()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        await ihr.PutAsJsonAsync("/resumes/me", new
        {
            positions = Array.Empty<object>(),
            education = Array.Empty<object>()
        });

        (await ihr.PutAsJsonAsync("/resumes/me/template", new { template = "modern" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await ihr.GetAsync("/resumes/me")))
            .GetProperty("template").GetString().Should().Be("modern");
    }

    /// <summary>
    /// <strong>Eine unbekannte Vorlage wird abgewiesen, nicht still gedreht.</strong>
    /// </summary>
    /// <remarks>
    /// Sonst wählt jemand etwas, bekommt 204 zurück und sieht danach etwas
    /// anderes — ein Fehler, der wie eine Meinungsänderung der Software
    /// aussieht.
    /// </remarks>
    [Fact]
    public async Task Eine_unbekannte_Vorlage_wird_abgewiesen()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        await ihr.PutAsJsonAsync("/resumes/me", new
        {
            positions = Array.Empty<object>(),
            education = Array.Empty<object>()
        });

        (await ihr.PutAsJsonAsync("/resumes/me/template", new { template = "glitzer" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// <strong>Die Löschung räumt Zeilen UND Dateien.</strong>
    /// </summary>
    /// <remarks>
    /// Die Zusage aus ADR-0027, an der Stelle, an der sie am leisesten bricht:
    /// die Oberfläche sieht nach einer Löschung leer aus, ob die Bytes noch
    /// daliegen oder nicht. Nur ein Blick auf die Platte unterscheidet das.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_der_Person_raeumt_auch_die_Ablage()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        await Lade(ihr, EinPdf, "a.pdf", "Eins");
        await Lade(ihr, EinPng, "b.png", "Zwei");

        DateienAufDerPlatte().Should().Be(2);

        var loeschen = _dienst.CreateClient();
        loeschen.DefaultRequestHeaders.Add("X-Erasure-Secret", "loesch-geheimnis");

        (await loeschen.PostAsJsonAsync("/erasure", new { user_id = anna })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await Json(await ihr.GetAsync("/resumes/me/documents"))).GetArrayLength().Should().Be(0);
        DateienAufDerPlatte().Should().Be(0);
    }
}
