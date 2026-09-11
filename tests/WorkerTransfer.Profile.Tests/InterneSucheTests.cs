using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Die Tür, durch die scout-service sucht.</summary>
/// <remarks>
/// <para>Sie liegt unter <c>/internal/</c>, hat <strong>keine
/// Gateway-Route</strong> und antwortet ohne das gemeinsame Geheimnis mit 404
/// statt 401 — ein 401 bestätigte, dass es den Endpunkt gibt.</para>
///
/// <para><strong>Sie prüft den Ledger nicht</strong>, und das ist der heikelste
/// Satz an ihr. Die Prüfung steht bei scout-service, für die ganze Seite auf
/// einmal (ADR-0030, ADR-0036 Entscheidung 1): genau EINE Stelle entscheidet
/// über Sichtbarkeit, zwei wären zwei Wahrheiten. Diese Reihe hält beides fest —
/// dass die Tür verschlossen ist, und dass sie ODER sucht statt UND.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class InterneSucheTests(Postgres postgres) : IAsyncLifetime
{
    private const string Meldegeheimnis = "melde-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probetor _tor = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("Notify:Geheimnis", Meldegeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Ohne das Geheimnis gibt es die Tür nicht.</summary>
    /// <remarks>
    /// 404 und nicht 401: ein 401 bestätigte, dass es den Endpunkt gibt. Und
    /// ein falsches Geheimnis antwortet genauso wie gar keines.
    /// </remarks>
    [Fact]
    public async Task Ohne_Geheimnis_gibt_es_die_Tuer_nicht()
    {
        var ohne = await _dienst.CreateClient().GetAsync(
            new Uri("/internal/profiles/search", UriKind.Relative));

        ohne.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var falsch = _dienst.CreateClient();
        falsch.DefaultRequestHeaders.Add("X-Notify-Secret", "daneben");

        (await falsch.GetAsync(new Uri("/internal/profiles/search", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await falsch.GetAsync(new Uri($"/internal/profiles/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Gesucht wird mit ODER: wer EINES der Worte nennt, ist dabei.
    /// </summary>
    /// <remarks>
    /// <para>Der Unterschied zu <c>GET /candidates</c>, das mit UND sucht — und
    /// er ist die Voraussetzung für die Häkchenliste des Scouts. Unter UND
    /// erfüllte jeder Treffer alle Bedingungen, jedes Häkchen wäre gesetzt, und
    /// „welche Fähigkeit fehlt" hätte keine Antwort (ADR-0036 Entscheidung 2).</para>
    ///
    /// <para>Die Gegenprobe steht daneben: dieselben Daten über
    /// <c>/candidates</c> gefragt liefern nur den, der beide nennt.</para>
    /// </remarks>
    [Fact]
    public async Task Die_interne_Suche_sucht_mit_ODER()
    {
        var eines = Guid.CreateVersion7();
        var beide = Guid.CreateVersion7();

        // Eigene Worte statt eigener Zahlen: die Reihen dieser Sammlung teilen
        // sich EINE Datenbank, und ein Zaehlen ueber „Go" waere davon abhaengig,
        // wer vorher lief.
        await Schreibe(eines, ["Buchbinderei"]);
        await Schreibe(beide, ["Buchbinderei", "Reetdach"]);

        var gefunden = await Intern(
            "/internal/profiles/search?skill=Buchbinderei&skill=Reetdach");

        gefunden.GetProperty("items").EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("subject_id").GetGuid())
            .Should().BeEquivalentTo([eines, beide]);

        // Und die Gegenprobe: `/candidates` sucht weiterhin mit UND.
        _tor.Frei.Add((eines, Firma));
        _tor.Frei.Add((beide, Firma));

        var kandidaten = await Json(await AlsFirma().GetAsync(
            new Uri("/candidates?skill=Buchbinderei&skill=Reetdach", UriKind.Relative)));

        kandidaten.GetProperty("items").EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("subject_id").GetGuid())
            .Should().Equal(beide);
    }

    /// <summary>Sie liefert genannte Fähigkeiten — und nichts über Sichtbarkeit.</summary>
    /// <remarks>
    /// Kein Sichtbarkeitsfeld im Rumpf (ADR-0020): das wäre eine zweite
    /// Wahrheit neben dem Ledger, und die beiden wären beim ersten Widerruf
    /// uneins. Der Aufrufer fragt ihn selbst.
    /// </remarks>
    [Fact]
    public async Task Ein_Fund_traegt_kein_Sichtbarkeitsfeld()
    {
        var wer = Guid.CreateVersion7();
        await Schreibe(wer, ["Reepschlagen"]);

        var fund = await Intern($"/internal/profiles/{wer}");

        fund.EnumerateObject().Select(feld => feld.Name).Should().BeEquivalentTo(
            "subject_id", "headline", "location", "remote_ok", "skills");

        fund.GetProperty("skills").EnumerateArray()
            .Select(eintrag => eintrag.GetString()).Should().Equal("Reepschlagen");
    }

    /// <summary>
    /// Sie gibt AUCH Profile heraus, die niemand freigegeben hat — mit Absicht.
    /// </summary>
    /// <remarks>
    /// <para>Die unbequemste Zusage dieser Tür, und deshalb steht sie als
    /// eigener Test da. Die Freigabe prüft scout-service, für die ganze Seite
    /// auf einmal. Wer diese Tür für etwas anderes benutzt, holt sich die
    /// Prüfung dazu — sonst zeigt er Profile, die niemand freigegeben hat.</para>
    ///
    /// <para>Sie ist genau deshalb hinter dem geteilten Geheimnis und ohne
    /// Gateway-Route: von aussen ist sie nicht erreichbar.</para>
    /// </remarks>
    [Fact]
    public async Task Sie_prueft_den_Ledger_nicht_und_das_ist_die_Zusage()
    {
        var wer = Guid.CreateVersion7();
        await Schreibe(wer, ["Rostschutz"]);

        var vorher = _tor.Fragen;

        var gefunden = await Intern("/internal/profiles/search?skill=Rostschutz");

        gefunden.GetProperty("items").EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("subject_id").GetGuid())
            .Should().Contain(wer, "niemand hat hier etwas freigegeben");

        _tor.Fragen.Should().Be(vorher, "diese Tür fragt den Ledger nicht");
    }

    /// <summary>Ein Profil, das es nicht gibt, ist ein 404.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Profil_ist_ein_404()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        (await browser.GetAsync(new Uri($"/internal/profiles/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------

    private static readonly Guid Firma = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private HttpClient AlsFirma()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", Tokenform.Firma(Guid.CreateVersion7(), Firma));
        return browser;
    }

    private async Task Schreibe(Guid wer, string[] faehigkeiten)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", Tokenform.Person(wer));

        var antwort = await browser.PutAsJsonAsync(
            new Uri("/profiles/me", UriKind.Relative),
            new
            {
                headline = "Entwicklerin",
                bio = "",
                location = "Berlin",
                remote_ok = true,
                skills = faehigkeiten
            });

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<JsonElement> Intern(string pfad)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        var antwort = await browser.GetAsync(new Uri(pfad, UriKind.Relative));

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Json(antwort);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;
}
