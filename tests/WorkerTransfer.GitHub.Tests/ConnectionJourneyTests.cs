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
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Beanspruchen, beweisen, zeigen, trennen.</summary>
[Collection(PostgresCollection.Name)]
public class ConnectionJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeGitHub _github = new();
    private readonly ProbeSignIn _signIn = new();
    private readonly Probeledger _ledger = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:github", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IGitHub>(_ => _github));
                services.Replace(ServiceDescriptor.Scoped<IGitHubSignIn>(_ => _signIn));
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsPerson(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Der <c>state</c> aus der Anmeldeadresse.</summary>
    private static string Zustand(string address) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(address).Query)["state"]!;

    private static Task<HttpResponseMessage> Verbinde(HttpClient browser, string login) =>
        browser.PostAsJsonAsync("/github/me", new { login });

    private static Repository Repo(
        string name, string? language = "Go", int sterne = 0, int tageAlt = 0) =>
        new(name, $"Ein Repository namens {name}", language, sterne,
            $"https://github.com/anna/{name}",
            DateTimeOffset.UnixEpoch.AddDays(1000 - tageAlt),
            language is null ? [] : [language], ["verteilte-systeme"]);

    /// <summary>Wer ein Konto GENANNT hat, muss dasselbe nachweisen.</summary>
    /// <remarks>
    /// Der Vergleich gilt weiterhin — nur eben dort, wo es etwas zu vergleichen
    /// gibt. Wer nichts genannt hat, bekommt den gemeldeten Namen eingetragen
    /// (siehe <see cref="Signing_in_without_a_naming_writes_in_the_reported_name" />).
    /// </remarks>
    [Fact]
    public async Task Name_sign_in_see()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var offen = await Json(await Verbinde(ihr, "anna-dev"));
        var state = offen.GetProperty("challenge_description").GetString()!
            .Replace("workertransfer-verify-", string.Empty, StringComparison.Ordinal);

        var beginn = await Json(await ihr.PostAsync("/github/me/oauth/start", null));
        beginn.GetProperty("url").GetString().Should().Contain(state);

        // Was der Browser bei GitHub täte, ist hier ein Code, der auf den
        // Anmeldenamen führt.
        _signIn.Codes["ein-code"] = "anna-dev";
        _github.Repos["anna-dev"] = [Repo("werkzeug")];

        var fertig = await Json(await ihr.PostAsJsonAsync(
            "/github/me/oauth/finish", new { code = "ein-code", state = state }));

        fertig.GetProperty("verified").GetBoolean().Should().BeTrue();
        fertig.GetProperty("challenge_description").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// <strong>Wer sich als jemand anderes anmeldet, beweist nichts.</strong>
    /// </summary>
    /// <remarks>
    /// Der gefährlichste Fall des ganzen Ablaufs: jemand trägt <c>torvalds</c>
    /// ein und meldet sich mit dem eigenen Konto an. Ohne den Namensvergleich
    /// stünde danach ein Nachweis für ein fremdes Konto — und dessen Arbeit
    /// erschiene als seine.
    /// </remarks>
    [Fact]
    public async Task Signing_in_with_a_foreign_account_proves_nothing()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var offen = await Json(await Verbinde(ihr, "torvalds"));
        var state = offen.GetProperty("challenge_description").GetString()!
            .Replace("workertransfer-verify-", string.Empty, StringComparison.Ordinal);

        _signIn.Codes["mein-code"] = "anna-dev";

        var response = await ihr.PostAsJsonAsync(
            "/github/me/oauth/finish", new { code = "mein-code", state = state });

        // 422 und nicht 404: die ResumeRequest war in Ordnung, der Nachweis fehlte.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var danach = await Json(await ihr.GetAsync("/github/me"));
        danach.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    /// <summary>Ein fremder Zustand beweist nichts.</summary>
    /// <remarks>
    /// Er ist der Schutz gegen eine untergeschobene Rückkehr: ohne ihn könnte
    /// jemand einer angemeldeten Person seinen eigenen Code unterschieben und
    /// ihr damit sein Konto anhängen.
    /// </remarks>
    [Fact]
    public async Task A_foreign_state_proves_nothing()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        await Verbinde(ihr, "anna-dev");
        _signIn.Codes["ein-code"] = "anna-dev";

        var response = await ihr.PostAsJsonAsync(
            "/github/me/oauth/finish",
            new { code = "ein-code", state = "ein-anderer-zustand" });

        // 422 und nicht 404: die ResumeRequest war in Ordnung, der Nachweis fehlte.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var danach = await Json(await ihr.GetAsync("/github/me"));
        danach.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    /// <summary>Ohne Zugangsdaten gibt es keine Adresse — und der Gist bleibt.</summary>
    [Fact]
    public async Task Without_a_configured_sign_in_there_is_no_address()
    {
        _signIn.IsConfigured = false;

        var ihr = AlsPerson(Guid.CreateVersion7());
        await Verbinde(ihr, "anna-dev");

        var beginn = await Json(await ihr.PostAsync("/github/me/oauth/start", null));

        beginn.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Ohne genanntes Konto führt die Anmeldung trotzdem hin.</summary>
    /// <remarks>
    /// Vorher stand hier die umgekehrte Erwartung, und sie war die Anforderung
    /// des GISTS an einem Ort, an dem der Gist nichts zu suchen hat: er muss
    /// wissen, in wessen Gists er sucht — die Anmeldung nicht, denn GitHub
    /// meldet allein das Konto, das zugestimmt hat. Gemessen am 05.09.2026 an
    /// einem echten Konto: der Namensschritt hat niemanden geschützt, sondern
    /// die Person ausgesperrt, auf deren Connection noch ein alter Name stand.
    /// </remarks>
    [Fact]
    public async Task Without_a_named_account_the_sign_in_still_leads_there()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var beginn = await Json(await ihr.PostAsync("/github/me/oauth/start", null));

        beginn.GetProperty("url").GetString().Should().NotBeNullOrEmpty();

        // Und die Connection, die dabei entsteht, BEHAUPTET NICHTS: kein
        // Konto, kein Nachweis.
        var danach = await Json(await ihr.GetAsync("/github/me"));
        danach.GetProperty("login").ValueKind.Should().Be(JsonValueKind.Null);
        danach.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    /// <summary>Wer nichts genannt hat, bekommt den gemeldeten Namen.</summary>
    /// <remarks>
    /// Der Ersatz für den Namensvergleich, und er ist nicht schwächer: GitHub
    /// meldet ausschliesslich das Konto, das die Zustimmung erteilt hat. Ein
    /// fremdes lässt sich hier nicht unterschieben, weil es gar nicht erst
    /// genannt werden kann.
    /// </remarks>
    [Fact]
    public async Task Signing_in_without_a_naming_writes_in_the_reported_name()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var beginn = await Json(await ihr.PostAsync("/github/me/oauth/start", null));
        var state = Zustand(beginn.GetProperty("url").GetString()!);

        _signIn.Codes["ein-code"] = "anna-dev";
        _github.Repos["anna-dev"] = [Repo("werkzeug")];

        var fertig = await Json(await ihr.PostAsJsonAsync(
            "/github/me/oauth/finish", new { code = "ein-code", state = state }));

        fertig.GetProperty("login").GetString().Should().Be("anna-dev");
        fertig.GetProperty("verified").GetBoolean().Should().BeTrue();
    }

    /// <summary>Die Auskunft, ob dieser Server die Anmeldung anbietet.</summary>
    /// <remarks>
    /// Sie legt nichts an — im Gegensatz zu <c>/me/oauth/start</c>. Genau
    /// deshalb darf die Oberfläche sie beim Betrachten der Seite stellen,
    /// ohne für jeden Besuch eine Zeile zu hinterlassen.
    /// </remarks>
    [Fact]
    public async Task The_configuration_can_be_asked_for_and_creates_nothing()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var gefragt = await Json(await ihr.GetAsync("/github/oauth"));
        gefragt.GetProperty("available").GetBoolean().Should().BeTrue();

        // Ohne Connection antwortet `/github/me` mit 200 und JSON-`null` —
        // die Frage ist erlaubt, die Antwort lautet „keine".
        (await Json(await ihr.GetAsync("/github/me"))).ValueKind
            .Should().Be(JsonValueKind.Null);

        _signIn.IsConfigured = false;

        var ohne = await Json(await ihr.GetAsync("/github/oauth"));
        ohne.GetProperty("available").GetBoolean().Should().BeFalse();
    }

    /// <summary>Der ganze Weg: nennen, Gist anlegen, beweisen, sehen.</summary>
    [Fact]
    public async Task Name_prove_see()
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
    public async Task On_naming_GitHub_is_not_asked()
    {
        await Verbinde(AlsPerson(Guid.CreateVersion7()), "fremdes-konto");

        _github.Asked.Should().BeEmpty();
    }

    /// <summary>Ohne Gist kein Nachweis — und das ist 422, nicht 404.</summary>
    /// <remarks>
    /// Die ResumeRequest war in Ordnung, der Nachweis fehlte.
    /// </remarks>
    [Fact]
    public async Task Without_a_gist_it_is_422()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        await Verbinde(ihr, "anna-dev");

        var attempt = await ihr.PostAsync("/github/me/verify", null);

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
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
    public async Task A_different_account_resets_the_proof()
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
    public async Task A_different_spelling_is_not_a_different_account()
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
    public async Task On_an_account_change_the_old_string_no_longer_holds()
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
    /// Sterne messen Visibility, nicht Arbeit, und eine Sortierung ist
    /// bereits eine Wertung (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task Sorted_by_date_not_by_stars()
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
            .Select(entry => entry.GetProperty("name").GetString())
            .ToList();

        namen.Should().Equal("neu-und-still", "alt-und-beliebt");
    }

    /// <summary>
    /// Ein Repository ohne Datum steht hinten, nicht vorn.
    /// </summary>
    [Fact]
    public async Task A_repository_without_a_date_stands_last()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        _github.Repos["anna-dev"] =
        [
            new Repository("ohne-datum", string.Empty, null, 0, "https://x", null, [], []),
            Repo("mit-datum", tageAlt: 900)
        ];

        await Bewiesen(ihr, "anna-dev");

        var namen = (await Json(await ihr.GetAsync("/github/me")))
            .GetProperty("repositories").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .ToList();

        namen.Should().Equal("mit-datum", "ohne-datum");
    }

    /// <summary>Die eigene Ansicht zeigt auch die unbewiesene Connection.</summary>
    /// <remarks>
    /// Sonst sähe die Person nach dem ersten Schritt gar nichts und wüsste
    /// nicht, welche Zeichenfolge sie in den Gist schreiben soll.
    /// </remarks>
    [Fact]
    public async Task Ones_own_view_shows_the_unproved_one_too()
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
    public async Task Without_a_connection_ones_own_view_is_null()
    {
        var response = await AlsPerson(Guid.CreateVersion7()).GetAsync("/github/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>
    /// Eine unbewiesene Connection ist von außen nicht vorhanden — auch mit
    /// Release.
    /// </summary>
    /// <remarks>
    /// Sie ist eine Behauptung, und Behauptungen zeigt dieser Dienst nicht.
    /// </remarks>
    [Fact]
    public async Task An_unproved_connection_does_not_exist_from_outside()
    {
        var anna = Guid.CreateVersion7();
        await Verbinde(AlsPerson(anna), "anna-dev");
        _ledger.Frei.Add(anna);

        var fremd = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        fremd.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ohne Release sieht ein Fremder nichts — und dasselbe wie bei niemandem.</summary>
    [Fact]
    public async Task Without_a_release_a_stranger_sees_nothing()
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

    /// <summary>Mit Release sieht ein Fremder die Belege — aber nie die Zeichenfolge.</summary>
    [Fact]
    public async Task With_a_release_a_stranger_sees_the_evidence()
    {
        var anna = Guid.CreateVersion7();
        _github.Repos["anna-dev"] = [Repo("werkzeug")];
        await Bewiesen(AlsPerson(anna), "anna-dev");
        _ledger.Frei.Add(anna);

        var gesehen = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        gesehen.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(gesehen);
        body.GetProperty("repositories").GetArrayLength().Should().Be(1);
        body.GetProperty("challenge_description").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Trennen heißt löschen — der Abzug verschwindet mit.</summary>
    [Fact]
    public async Task Disconnecting_means_erasing()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);
        _github.Repos["anna-dev"] = [Repo("werkzeug")];
        await Bewiesen(ihr, "anna-dev");

        var getrennt = await ihr.DeleteAsync("/github/me");

        getrennt.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var context = postgres.Context();
        (await context.Connections.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>
    /// IsSilent GitHub, wird niemandem der Nachweis abgesprochen.
    /// </summary>
    [Fact]
    public async Task A_silent_GitHub_becomes_503_and_not_422()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());
        await Verbinde(ihr, "anna-dev");
        _github.IsSilent = true;

        var attempt = await ihr.PostAsync("/github/me/verify", null);

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>IsSilent der Ledger, wird weder gezeigt noch geleugnet.</summary>
    [Fact]
    public async Task A_silent_ledger_becomes_503_and_not_404()
    {
        var anna = Guid.CreateVersion7();
        await Bewiesen(AlsPerson(anna), "anna-dev");
        _ledger.IsSilent = true;

        var attempt = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
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
    public async Task An_impossible_user_name_is_422(string login)
    {
        var response = await Verbinde(AlsPerson(Guid.CreateVersion7()), login);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _github.Asked.Should().BeEmpty("ein Abruf, der ohnehin nichts findet, entfällt");
    }

    /// <summary>Das führende @ ist Gewohnheit, kein Fehler.</summary>
    [Fact]
    public async Task A_leading_at_sign_is_stripped()
    {
        var genannt = await Verbinde(AlsPerson(Guid.CreateVersion7()), "@anna-dev");

        genannt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(genannt)).GetProperty("login").GetString().Should().Be("anna-dev");
    }

    /// <summary>Ohne Anmeldung geht nichts.</summary>
    [Fact]
    public async Task Without_a_token_it_is_401()
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
