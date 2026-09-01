using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace WorkerTransfer.Consent.Tests;

/// <summary>What arrives here when somebody deletes their account.</summary>
/// <remarks>
/// This service is the one recipient where something deliberately <em>stays</em>
/// (ADR-0027 §5): the ledger is the proof that the erasure happened. Deleting it
/// along with everything else would make the promise unprovable — "we deleted"
/// with nothing left to check it against.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class LoeschempfangTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "loesch-geheimnis-fuer-den-test";

    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = Dienst(Geheimnis);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> Dienst(string geheimnis) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:consent", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", geheimnis);
            host.UseSetting("environment", "Development");
        });

    private HttpClient Als(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Fuer(wer));
        return browser;
    }

    private static HttpRequestMessage Loeschbefehl(Guid wer, string? geheimnis)
    {
        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = wer })
        };

        if (geheimnis is not null)
        {
            anfrage.Headers.Add("X-Erasure-Secret", geheimnis);
        }

        return anfrage;
    }

    private async Task<Guid> PersonMitFreigaben()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);

        await browser.PostAsJsonAsync("/consent/grant", new
        {
            subject_id = anna, capability = "profile.visibility:public"
        });
        await browser.PostAsJsonAsync("/consent/grant", new
        {
            subject_id = anna, capability = "portfolio.visibility:public"
        });
        await browser.PostAsJsonAsync("/consent/revoke", new
        {
            subject_id = anna,
            capability = "portfolio.visibility:public",
            reason = "mein Arbeitgeber soll das nicht sehen"
        });

        return anna;
    }

    private async Task<List<(string Action, string? Reason)>> Zeilen(Guid wer)
    {
        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText =
            "SELECT action::text, reason FROM consent_events WHERE subject_id = @wer ORDER BY id";
        befehl.Parameters.AddWithValue("wer", wer);

        var zeilen = new List<(string, string?)>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            zeilen.Add((leser.GetString(0), leser.IsDBNull(1) ? null : leser.GetString(1)));
        }

        return zeilen;
    }

    /// <summary>
    /// A default that opens in case of doubt would be the worst possible one
    /// for a route that erases a person — and an unset variable is exactly the
    /// case of doubt.
    /// </summary>
    [Fact]
    public async Task Ein_leeres_Geheimnis_schliesst_den_Endpunkt()
    {
        using var ohne = Dienst(string.Empty);

        var antwort = await ohne.CreateClient().SendAsync(
            Loeschbefehl(Guid.CreateVersion7(), "irgendwas"));

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("falsch")]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_geloescht(string? vorgelegt)
    {
        var anna = await PersonMitFreigaben();

        var antwort = await _dienst.CreateClient().SendAsync(Loeschbefehl(anna, vorgelegt));

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Zeilen(anna)).Should().NotContain(zeile => zeile.Action == "DELETE");
    }

    /// <summary>
    /// The chain stays as proof, one closing fact per capability ever held is
    /// appended, and the free text is cleared. The argument is not "we may
    /// retain" but that nothing maps a subject id to a person afterwards.
    /// </summary>
    [Fact]
    public async Task Die_Kette_bleibt_der_Freitext_geht()
    {
        var anna = await PersonMitFreigaben();

        var antwort = await _dienst.CreateClient().SendAsync(Loeschbefehl(anna, Geheimnis));

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var zeilen = await Zeilen(anna);

        zeilen.Should().Contain(zeile => zeile.Action == "GRANT", "die Kette bleibt");
        zeilen.Should().Contain(zeile => zeile.Action == "REVOKE");
        zeilen.Count(zeile => zeile.Action == "DELETE")
            .Should().Be(2, "je jemals gehaltener Faehigkeit eine Schlusszeile");
        zeilen.Should().OnlyContain(
            zeile => zeile.Reason == null, "der Freitext ist ueberall geleert");
    }

    [Fact]
    public async Task Nach_der_Loeschung_gilt_keine_Freigabe_mehr()
    {
        var anna = await PersonMitFreigaben();

        await _dienst.CreateClient().SendAsync(Loeschbefehl(anna, Geheimnis));

        var geprueft = await Als(Guid.CreateVersion7()).PostAsJsonAsync(
            "/consent/check", new { subject_id = anna, capability = "profile.visibility:public" });

        var stand = JsonDocument.Parse(await geprueft.Content.ReadAsStringAsync()).RootElement;

        stand.GetProperty("granted").GetBoolean().Should().BeFalse();
        stand.GetProperty("deleted").GetBoolean().Should().BeTrue(
            "geloescht ist nicht dasselbe wie widerrufen");
    }

    /// <summary>
    /// The cascade delivers at least once, so a redelivery has to be harmless —
    /// otherwise a dispatcher that crashes after delivering and before ticking
    /// off would double every closing fact.
    /// </summary>
    [Fact]
    public async Task Zweimal_loeschen_aendert_nichts_mehr()
    {
        var anna = await PersonMitFreigaben();

        await _dienst.CreateClient().SendAsync(Loeschbefehl(anna, Geheimnis));
        var nachher = await Zeilen(anna);

        var zweite = await _dienst.CreateClient().SendAsync(Loeschbefehl(anna, Geheimnis));

        zweite.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Zeilen(anna)).Should().HaveCount(nachher.Count);
    }

    /// <summary>
    /// Nothing personal was retained, so the receipt says zero — even though
    /// the chain stays.
    /// </summary>
    /// <remarks>
    /// The two are not the same thing, and confusing them would be the easiest
    /// way to make this endpoint lie. <c>retained</c> counts rows a
    /// <em>retention switch</em> kept from deletion (ADR-0027 §3) — hired
    /// applications, paid transfers. The consent chain is not that: it is
    /// anonymised in place, and afterwards nothing maps a subject id to a
    /// person. A positive count here would tell the origin that data about
    /// somebody survived, which is exactly what did not happen.
    /// </remarks>
    [Fact]
    public async Task Die_Quittung_meldet_null_denn_nichts_Personenbezogenes_blieb()
    {
        var anna = await PersonMitFreigaben();

        var antwort = await _dienst.CreateClient().SendAsync(Loeschbefehl(anna, Geheimnis));
        var quittung = JsonDocument.Parse(
            await antwort.Content.ReadAsStringAsync()).RootElement;

        quittung.GetProperty("retained").GetInt32().Should().Be(0);
    }
}
