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
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Beanspruchen, beweisen, zeigen, trennen.</summary>
[Collection(PostgresCollection.Name)]
public class VerbindungsreiseTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeGitHub _github = new();
    private readonly Probeledger _ledger = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:github", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IGitHub>(_ => _github));
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _ledger));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
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

    private static Task<HttpResponseMessage> Verbinde(HttpClient browser, string login) =>
        browser.PostAsJsonAsync("/github/me", new { login });

    private static Repository Repo(
        string name, string? sprache = "Go", int sterne = 0, int tageAlt = 0) =>
        new(name, $"Ein Repository namens {name}", sprache, sterne,
            $"https://github.com/anna/{name}",
            DateTimeOffset.UnixEpoch.AddDays(1000 - tageAlt));

    /// <summary>Der ganze Weg: nennen, Gist anlegen, beweisen, sehen.</summary>
    [Fact]
    public async Task Nennen_beweisen_sehen()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        var genannt = await Verbinde(ihr, "anna-dev");

        genannt.StatusCode.Should().Be(HttpStatusCode.OK);

        var offen = await Json(genannt);
        offen.GetProperty("verified").GetBoolean().Should().BeFalse();

        var beschreibung = offen.GetProperty("challenge_description").GetString();
        beschreibung.Should().StartWith("workertransfer-verify-");

        // Die Person legt den Gist an — das ist der ganze Nachweis.
        _github.Gists["anna-dev"] = [beschreibung!];
        _github.Repos["anna-dev"] = [Repo("werkzeug"), Repo("bibliothek")];

        var bewiesen = await ihr.PostAsync("/github/me/verify", null);

        bewiesen.StatusCode.Should().Be(HttpStatusCode.OK);

        var fertig = await Json(bewiesen);
        fertig.GetProperty("verified").GetBoolean().Should().BeTrue();
        fertig.GetProperty("repositories").GetArrayLength().Should().Be(2);

        // Bewiesen heißt: die Einmalzeichenfolge nützt niemandem mehr.
        fertig.GetProperty("challenge_description").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Beim Nennen wird NICHT bei GitHub angefragt.
    /// </summary>
    /// <remarks>
    /// Solange nichts bewiesen ist, gibt es nichts zu holen — und ein Abruf
    /// verriete GitHub nur, dass jemand nach diesem Konto gefragt hat.
    /// </remarks>
    [Fact]
    public async Task Beim_Nennen_wird_GitHub_nicht_gefragt()
    {
        await Verbinde(AlsPerson(Guid.CreateVersion7()), "fremdes-konto");

        _github.Gefragt.Should().BeEmpty();
    }

    /// <summary>Ohne Gist kein Nachweis — und das ist 422, nicht 404.</summary>
    /// <remarks>
    /// Die Anfrage war in Ordnung, der Nachweis fehlte.
    /// </remarks>
    [Fact]
    public async Task Ohne_Gist_ist_es_422()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        await Verbinde(ihr, "anna-dev");

        var versuch = await ihr.PostAsync("/github/me/verify", null);

        versuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Ein anderes Konto zu nennen setzt den Nachweis zurück.
    /// </summary>
    /// <remarks>
    /// Ohne dieses Zurücksetzen könnte jemand ein Konto nachweisen und danach
    /// den Namen auf ein fremdes ändern: der Nachweis stünde noch, wäre aber
    /// für ein anderes Konto erbracht worden.
    /// </remarks>
    [Fact]
    public async Task Ein_anderes_Konto_setzt_den_Nachweis_zurueck()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);
        await Bewiesen(ihr, "anna-dev");

        var umbenannt = await Json(await Verbinde(ihr, "jemand-anderes"));

        umbenannt.GetProperty("verified").GetBoolean().Should().BeFalse();
        umbenannt.GetProperty("repositories").GetArrayLength().Should().Be(0);
        umbenannt.GetProperty("challenge_description").ValueKind
            .Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Eine andere Schreibweise ist kein anderes Konto — der Nachweis gilt
    /// weiter.
    /// </summary>
    [Fact]
    public async Task Eine_andere_Schreibweise_ist_kein_anderes_Konto()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        await Bewiesen(ihr, "anna-dev");

        var gleich = await Json(await Verbinde(ihr, "Anna-Dev"));

        gleich.GetProperty("verified").GetBoolean().Should().BeTrue();
        gleich.GetProperty("login").GetString().Should().Be("Anna-Dev");
    }

    /// <summary>
    /// Die neue Einmalzeichenfolge ist eine neue — die alte darf nicht
    /// weitergelten.
    /// </summary>
    /// <remarks>
    /// Sonst bewiese ein Gist, der für das alte Konto angelegt wurde, das
    /// neue mit.
    /// </remarks>
    [Fact]
    public async Task Beim_Kontowechsel_gilt_die_alte_Zeichenfolge_nicht_weiter()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        var alte = (await Json(await Verbinde(ihr, "anna-dev")))
            .GetProperty("challenge_description").GetString();

        var neue = (await Json(await Verbinde(ihr, "jemand-anderes")))
            .GetProperty("challenge_description").GetString();

        neue.Should().NotBe(alte);

        // Ein Gist mit der ALTEN Zeichenfolge beweist das neue Konto nicht.
        _github.Gists["jemand-anderes"] = [alte!];

        (await ihr.PostAsync("/github/me/verify", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Neueste zuerst — nicht nach Sternen.</summary>
    /// <remarks>
    /// Sterne messen Sichtbarkeit, nicht Arbeit, und eine Sortierung ist
    /// bereits eine Wertung (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task Sortiert_wird_nach_Datum_nicht_nach_Sternen()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        _github.Repos["anna-dev"] =
        [
            Repo("alt-und-beliebt", sterne: 9000, tageAlt: 500),
            Repo("neu-und-still", sterne: 0, tageAlt: 1)
        ];

        await Bewiesen(ihr, "anna-dev");

        var namen = (await Json(await ihr.GetAsync("/github/me")))
            .GetProperty("repositories").EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("name").GetString())
            .ToList();

        namen.Should().Equal("neu-und-still", "alt-und-beliebt");
    }

    /// <summary>
    /// Ein Repository ohne Datum steht hinten, nicht vorn.
    /// </summary>
    [Fact]
    public async Task Ein_Repository_ohne_Datum_steht_hinten()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        _github.Repos["anna-dev"] =
        [
            new Repository("ohne-datum", string.Empty, null, 0, "https://x", null),
            Repo("mit-datum", tageAlt: 900)
        ];

        await Bewiesen(ihr, "anna-dev");

        var namen = (await Json(await ihr.GetAsync("/github/me")))
            .GetProperty("repositories").EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("name").GetString())
            .ToList();

        namen.Should().Equal("mit-datum", "ohne-datum");
    }

    /// <summary>Die eigene Ansicht zeigt auch die unbewiesene Verbindung.</summary>
    /// <remarks>
    /// Sonst sähe die Person nach dem ersten Schritt gar nichts und wüsste
    /// nicht, welche Zeichenfolge sie in den Gist schreiben soll.
    /// </remarks>
    [Fact]
    public async Task Die_eigene_Ansicht_zeigt_auch_die_unbewiesene()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        await Verbinde(ihr, "anna-dev");

        var meine = await Json(await ihr.GetAsync("/github/me"));

        meine.GetProperty("verified").GetBoolean().Should().BeFalse();
        meine.GetProperty("challenge_description").ValueKind
            .Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>Wer nichts genannt hat, bekommt `null`.</summary>
    [Fact]
    public async Task Ohne_Verbindung_ist_die_eigene_Ansicht_null()
    {
        var antwort = await AlsPerson(Guid.CreateVersion7()).GetAsync("/github/me");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await antwort.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>
    /// Eine unbewiesene Verbindung ist von außen nicht vorhanden — auch mit
    /// Freigabe.
    /// </summary>
    /// <remarks>
    /// Sie ist eine Behauptung, und Behauptungen zeigt dieser Dienst nicht.
    /// </remarks>
    [Fact]
    public async Task Eine_unbewiesene_Verbindung_ist_von_aussen_nicht_da()
    {
        var anna = Guid.CreateVersion7();
        await Verbinde(AlsPerson(anna), "anna-dev");
        _ledger.Frei.Add(anna);

        var fremd = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        fremd.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ohne Freigabe sieht ein Fremder nichts — und dasselbe wie bei niemandem.</summary>
    [Fact]
    public async Task Ohne_Freigabe_sieht_ein_Fremder_nichts()
    {
        var anna = Guid.CreateVersion7();
        await Bewiesen(AlsPerson(anna), "anna-dev");

        var fremder = AlsPerson(Guid.CreateVersion7());
        var verborgen = await fremder.GetAsync($"/github/{anna}");
        var niemand = await fremder.GetAsync($"/github/{Guid.CreateVersion7()}");

        verborgen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        niemand.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(verborgen)).GetProperty("detail").GetString()
            .Should().Be((await Json(niemand)).GetProperty("detail").GetString());
    }

    /// <summary>Mit Freigabe sieht ein Fremder die Belege — aber nie die Zeichenfolge.</summary>
    [Fact]
    public async Task Mit_Freigabe_sieht_ein_Fremder_die_Belege()
    {
        var anna = Guid.CreateVersion7();
        _github.Repos["anna-dev"] = [Repo("werkzeug")];
        await Bewiesen(AlsPerson(anna), "anna-dev");
        _ledger.Frei.Add(anna);

        var gesehen = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        gesehen.StatusCode.Should().Be(HttpStatusCode.OK);

        var rumpf = await Json(gesehen);
        rumpf.GetProperty("repositories").GetArrayLength().Should().Be(1);
        rumpf.GetProperty("challenge_description").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Trennen heißt löschen — der Abzug verschwindet mit.</summary>
    [Fact]
    public async Task Trennen_heisst_loeschen()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);
        _github.Repos["anna-dev"] = [Repo("werkzeug")];
        await Bewiesen(ihr, "anna-dev");

        var getrennt = await ihr.DeleteAsync("/github/me");

        getrennt.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var kontext = postgres.Kontext();
        (await kontext.Verbindungen.AnyAsync(zeile => zeile.Id == anna)).Should().BeFalse();
    }

    /// <summary>
    /// Schweigt GitHub, wird niemandem der Nachweis abgesprochen.
    /// </summary>
    [Fact]
    public async Task Ein_schweigendes_GitHub_wird_503_und_nicht_422()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        await Verbinde(ihr, "anna-dev");
        _github.Schweigt = true;

        var versuch = await ihr.PostAsync("/github/me/verify", null);

        versuch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Schweigt der Ledger, wird weder gezeigt noch geleugnet.</summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_wird_503_und_nicht_404()
    {
        var anna = Guid.CreateVersion7();
        await Bewiesen(AlsPerson(anna), "anna-dev");
        _ledger.Schweigt = true;

        var versuch = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        versuch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Ein Benutzername, den GitHub nie vergeben würde, wird abgewiesen.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-anna")]
    [InlineData("anna-")]
    [InlineData("an--na")]
    [InlineData("anna dev")]
    [InlineData("../../etc/passwd")]
    [InlineData("anna@dev")]
    public async Task Ein_unmoeglicher_Benutzername_ist_422(string login)
    {
        var antwort = await Verbinde(AlsPerson(Guid.CreateVersion7()), login);

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _github.Gefragt.Should().BeEmpty("ein Abruf, der ohnehin nichts findet, entfällt");
    }

    /// <summary>Das führende @ ist Gewohnheit, kein Fehler.</summary>
    [Fact]
    public async Task Ein_fuehrendes_At_wird_abgestreift()
    {
        var genannt = await Verbinde(AlsPerson(Guid.CreateVersion7()), "@anna-dev");

        genannt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(genannt)).GetProperty("login").GetString().Should().Be("anna-dev");
    }

    /// <summary>Ohne Anmeldung geht nichts.</summary>
    [Fact]
    public async Task Ohne_Token_ist_es_401()
    {
        (await _dienst.CreateClient().GetAsync("/github/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _dienst.CreateClient().GetAsync($"/github/{Guid.CreateVersion7()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Nennt ein Konto und beweist es.</summary>
    private async Task Bewiesen(HttpClient browser, string login)
    {
        var beschreibung = (await Json(await Verbinde(browser, login)))
            .GetProperty("challenge_description").GetString();

        _github.Gists[login] = [beschreibung!];

        (await browser.PostAsync("/github/me/verify", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
