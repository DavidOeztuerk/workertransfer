using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Consent.Tests;

/// <summary>The ledger over HTTP, against the real schema.</summary>
[Collection(PostgresCollection.Name)]
public class LedgerreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "loesch-geheimnis-fuer-den-test";

    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:consent", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Geheimnis);
            host.UseSetting("environment", "Development");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Als(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Fuer(wer));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Erteile(
        HttpClient browser, Guid wer, string faehigkeit) =>
        browser.PostAsJsonAsync(
            "/consent/grant", new { subject_id = wer, capability = faehigkeit });

    private static Task<HttpResponseMessage> Widerrufe(
        HttpClient browser, Guid wer, string faehigkeit, string grund) =>
        browser.PostAsJsonAsync(
            "/consent/revoke", new { subject_id = wer, capability = faehigkeit, reason = grund });

    private static Task<HttpResponseMessage> Pruefe(
        HttpClient browser, Guid wer, string faehigkeit) =>
        browser.PostAsJsonAsync(
            "/consent/check", new { subject_id = wer, capability = faehigkeit });

    [Fact]
    public async Task Erteilen_widerrufen_und_wieder_erteilen()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);

        (await Erteile(browser, anna, "profile.visibility:public")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await Json(await Pruefe(browser, anna, "profile.visibility:public")))
            .GetProperty("granted").GetBoolean().Should().BeTrue();

        (await Widerrufe(browser, anna, "profile.visibility:public", "erstmal nicht"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await Pruefe(browser, anna, "profile.visibility:public")))
            .GetProperty("granted").GetBoolean().Should().BeFalse();

        // Withdrawing must never be a one-way door for the person it belongs to.
        (await Erteile(browser, anna, "profile.visibility:public")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await Json(await Pruefe(browser, anna, "profile.visibility:public")))
            .GetProperty("granted").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Absence is a state, not an error: a capability nobody granted is simply
    /// not granted. Consuming services must be able to ask about anything.
    /// </summary>
    [Fact]
    public async Task Eine_nie_beruehrte_Faehigkeit_ist_200_und_nicht_404()
    {
        var wer = Guid.CreateVersion7();

        var antwort = await Pruefe(Als(Guid.CreateVersion7()), wer, "profile.visibility:public");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("granted").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Strict self-management: there is no delegation model, and admin or
    /// guardian consent is a later, deliberate decision rather than something
    /// allowed by omission.
    /// </summary>
    [Fact]
    public async Task Niemand_erteilt_fuer_einen_anderen()
    {
        var anna = Guid.CreateVersion7();
        var fremder = Guid.CreateVersion7();

        var versuch = await Erteile(Als(fremder), anna, "profile.visibility:public");

        versuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Open to any authenticated caller about any person — that is what makes
    /// the ledger usable as an enabler by consuming services.
    /// </summary>
    [Fact]
    public async Task Jeder_Angemeldete_darf_ueber_jeden_fragen()
    {
        var anna = Guid.CreateVersion7();
        await Erteile(Als(anna), anna, "profile.visibility:public");

        var antwort = await Pruefe(Als(Guid.CreateVersion7()), anna, "profile.visibility:public");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("granted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Ohne_Anmeldung_beantwortet_der_Ledger_nichts()
    {
        var antwort = await _dienst.CreateClient().PostAsJsonAsync(
            "/consent/check",
            new { subject_id = Guid.CreateVersion7(), capability = "profile.visibility:public" });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The reason is free text a person wrote about themselves. It must not
    /// ride along on a query anyone may issue.
    /// </summary>
    [Fact]
    public async Task Die_Pruefung_verraet_den_Widerrufsgrund_nicht()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);
        const string grund = "mein Arbeitgeber soll das nicht sehen";

        await Erteile(browser, anna, "profile.visibility:public");
        await Widerrufe(browser, anna, "profile.visibility:public", grund);

        var fremd = await Pruefe(Als(Guid.CreateVersion7()), anna, "profile.visibility:public");
        var rumpf = await fremd.Content.ReadAsStringAsync();

        rumpf.Should().NotContain("Arbeitgeber");
        rumpf.Should().NotContain("reason");
    }

    /// <summary>
    /// The opposite case: towards the person themselves there is no ground to
    /// withhold what they wrote. That is the difference between "belongs to
    /// them" and "is anybody else's business".
    /// </summary>
    [Fact]
    public async Task Die_eigene_Geschichte_zeigt_den_Grund_sehr_wohl()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);
        const string grund = "mein Arbeitgeber soll das nicht sehen";

        await Erteile(browser, anna, "profile.visibility:public");
        await Widerrufe(browser, anna, "profile.visibility:public", grund);

        var eigene = await browser.GetAsync("/consent/me/history");

        (await eigene.Content.ReadAsStringAsync()).Should().Contain(grund);
    }

    /// <summary>
    /// A foreign list would say which OTHER companies hold access to somebody.
    /// The endpoint takes no subject at all — not in the path, not as a query.
    /// </summary>
    [Fact]
    public async Task Die_eigene_Liste_nimmt_keinen_fremden_Gegenstand_entgegen()
    {
        var anna = Guid.CreateVersion7();
        var fremder = Guid.CreateVersion7();
        await Erteile(Als(anna), anna, "profile.visibility:public");

        var meine = await Json(await Als(fremder)
            .GetAsync($"/consent/me?subject_id={anna}"));

        meine.GetArrayLength().Should().Be(
            0, "der Parameter wird nicht gelesen, es ist die Liste des Aufrufers");
    }

    [Fact]
    public async Task Die_eigene_Liste_zeigt_nur_was_gerade_gilt()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);

        await Erteile(browser, anna, "profile.visibility:public");
        await Erteile(browser, anna, "portfolio.visibility:public");
        await Widerrufe(browser, anna, "portfolio.visibility:public", "doch nicht");

        var meine = await Json(await browser.GetAsync("/consent/me"));

        meine.GetArrayLength().Should().Be(1);
        meine[0].GetProperty("capability").GetString().Should().Be("profile.visibility:public");
    }

    /// <summary>
    /// The order is part of the contract: the caller maps answers onto its rows,
    /// and a pair asked twice would otherwise be ambiguous.
    /// </summary>
    [Fact]
    public async Task Die_Sammelpruefung_antwortet_in_der_Reihenfolge_der_Fragen()
    {
        var anna = Guid.CreateVersion7();
        var berta = Guid.CreateVersion7();
        await Erteile(Als(anna), anna, "profile.visibility:public");

        var antwort = await Als(Guid.CreateVersion7()).PostAsJsonAsync("/consent/check-batch", new
        {
            pairs = new[]
            {
                new { subject_id = berta, capability = "profile.visibility:public" },
                new { subject_id = anna, capability = "profile.visibility:public" },
                new { subject_id = berta, capability = "portfolio.visibility:public" }
            }
        });

        var ergebnisse = (await Json(antwort)).GetProperty("results");

        ergebnisse.GetArrayLength().Should().Be(3, "eine Antwort je Frage");
        ergebnisse[0].GetProperty("granted").GetBoolean().Should().BeFalse();
        ergebnisse[1].GetProperty("granted").GetBoolean().Should().BeTrue();
        ergebnisse[2].GetProperty("granted").GetBoolean().Should().BeFalse();
        ergebnisse[1].GetProperty("subject_id").GetGuid().Should().Be(anna);
    }

    /// <summary>
    /// A batch must not give away what the single question withholds.
    /// </summary>
    [Fact]
    public async Task Die_Sammelpruefung_verraet_ebenso_wenig()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);
        const string grund = "mein Arbeitgeber soll das nicht sehen";

        await Erteile(browser, anna, "profile.visibility:public");
        await Widerrufe(browser, anna, "profile.visibility:public", grund);

        var antwort = await Als(Guid.CreateVersion7()).PostAsJsonAsync("/consent/check-batch", new
        {
            pairs = new[] { new { subject_id = anna, capability = "profile.visibility:public" } }
        });

        (await antwort.Content.ReadAsStringAsync()).Should().NotContain("Arbeitgeber");
    }

    /// <summary>
    /// Derived, not chosen: one page is fifty people, two capabilities each. A
    /// batch makes asking cheaper, and a ceiling keeps that difference small.
    /// </summary>
    [Fact]
    public async Task Mehr_als_hundert_Paare_werden_abgewiesen()
    {
        var paare = Enumerable.Range(0, Einwilligungsgrenzen.HoechsteSammelgroesse + 1)
            .Select(_ => new
            {
                subject_id = Guid.CreateVersion7(),
                capability = "profile.visibility:public"
            })
            .ToArray();

        var antwort = await Als(Guid.CreateVersion7())
            .PostAsJsonAsync("/consent/check-batch", new { pairs = paare });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Genau_hundert_Paare_gehen_durch()
    {
        var paare = Enumerable.Range(0, Einwilligungsgrenzen.HoechsteSammelgroesse)
            .Select(_ => new
            {
                subject_id = Guid.CreateVersion7(),
                capability = "profile.visibility:public"
            })
            .ToArray();

        var antwort = await Als(Guid.CreateVersion7())
            .PostAsJsonAsync("/consent/check-batch", new { pairs = paare });

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("results").GetArrayLength()
            .Should().Be(Einwilligungsgrenzen.HoechsteSammelgroesse);
    }

    /// <summary>
    /// The canonical rule lives in memory, the read path reduces in SQL. Two
    /// ways to the same answer that can differ are worse than no second way, so
    /// this pins that they cannot.
    /// </summary>
    [Fact]
    public async Task Die_Liste_und_die_Pruefung_koennen_nicht_widersprechen()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);

        string[] alle =
        [
            "profile.visibility:public",
            "portfolio.visibility:public",
            "resume.visibility:tenant:9f8e7d6c-5b4a-4938-8271-605f4e3d2c1b"
        ];

        foreach (var faehigkeit in alle)
        {
            await Erteile(browser, anna, faehigkeit);
        }

        await Widerrufe(browser, anna, alle[1], "doch nicht");

        var liste = (await Json(await browser.GetAsync("/consent/me")))
            .EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("capability").GetString())
            .ToHashSet();

        foreach (var faehigkeit in alle)
        {
            var geprueft = (await Json(await Pruefe(browser, anna, faehigkeit)))
                .GetProperty("granted").GetBoolean();

            geprueft.Should().Be(
                liste.Contains(faehigkeit),
                $"Liste und Pruefung muessen sich ueber {faehigkeit} einig sein");
        }
    }

    /// <summary>
    /// It was capability-scoped, every capability here is a visibility, and
    /// equating the two would have erased a CV on a visibility withdrawal
    /// nobody asked for.
    /// </summary>
    [Fact]
    public async Task Es_gibt_kein_consent_delete()
    {
        var anna = Guid.CreateVersion7();

        var versuch = await Als(anna).PostAsJsonAsync(
            "/consent/delete",
            new { subject_id = anna, capability = "profile.visibility:public" });

        versuch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The shape is checked, the vocabulary is not.
    /// </summary>
    /// <remarks>
    /// A list of permitted capabilities would be a claim about which
    /// permissions can exist, and the ledger has to be able to answer about
    /// anything a consumer asks. So <c>profile..visibility</c> is odd but
    /// structurally fine and goes through — only what cannot be a token at all
    /// is refused.
    /// </remarks>
    [Theory]
    [InlineData("KEIN TOKEN")]
    [InlineData("Profile.Visibility")]
    [InlineData("1profile.visibility")]
    [InlineData("")]
    public async Task Was_keine_Faehigkeit_sein_kann_wird_abgewiesen(string faehigkeit)
    {
        var anna = Guid.CreateVersion7();

        var versuch = await Erteile(Als(anna), anna, faehigkeit);

        versuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
