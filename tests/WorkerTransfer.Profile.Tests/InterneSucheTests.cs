using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Die Tür, durch die scout-service sucht.</summary>
/// <remarks>
/// <para>Sie liegt unter <c>/internal/</c>, hat <strong>keine
/// Gateway-Route</strong> und antwortet ohne das gemeinsame Geheimnis mit 404
/// statt 401 — ein 401 bestätigte, dass es den Endpunkt gibt.</para>
///
/// <para><strong>Sie prüft den Ledger nicht</strong>, und das ist der heikelste
/// Satz an ihr. Die Prüfung steht bei scout-service, für die ganze Seite auf
/// einmal (ADR-0030, ADR-0036 Entscheidung 1): genau EINE Job entscheidet
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
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("Notify:Geheimnis", Meldegeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _tor)));
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
    /// <para><c>GET /candidates</c> suchte mit UND und ist am 11.09.2026
    /// gefallen; mit ihm der UND-Zweig im Speicher. Das ODER ist keine
    /// Lockerung, sondern die Voraussetzung für die Häkchenliste des Scouts:
    /// unter UND erfüllte jeder Hit alle Bedingungen, jedes Häkchen wäre
    /// gesetzt, und „welche Fähigkeit fehlt" hätte keine Antwort
    /// (ADR-0036 Entscheidung 2).</para>
    ///
    /// <para><strong>Die Gegenprobe steckt in der Zusage selbst:</strong> wer
    /// nur EINES der beiden Worte nennt, muss dabei sein. Waere es ein UND,
    /// kaeme nur der zurueck, der beide nennt — und diese Zeile faellt.</para>
    /// </remarks>
    [Fact]
    public async Task Die_interne_Suche_sucht_mit_ODER()
    {
        var eines = Guid.CreateVersion7();
        var beide = Guid.CreateVersion7();

        // Own Worte statt eigener Zahlen: die Reihen dieser Sammlung teilen
        // sich EINE Datenbank, und ein Zaehlen ueber „Go" waere davon abhaengig,
        // wer vorher lief.
        await Write(eines, ["Buchbinderei"]);
        await Write(beide, ["Buchbinderei", "Reetdach"]);

        var found = await Intern(
            "/internal/profiles/search?skill=Buchbinderei&skill=Reetdach");

        found.GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("subject_id").GetGuid())
            .Should().BeEquivalentTo(
                [eines, beide],
                "wer nur eines der beiden Worte nennt, gehoert dazu — sonst waere "
                + "es ein UND und jedes Haekchen des Scouts gesetzt");
    }

    /// <summary>Sie liefert genannte Fähigkeiten — und nichts über Sichtbarkeit.</summary>
    /// <remarks>
    /// Kein Sichtbarkeitsfeld im Rumpf (ADR-0020): das wäre eine zweite
    /// Wahrheit neben dem Ledger, und die beiden wären beim ersten Revocation
    /// uneins. Der Aufrufer fragt ihn selbst.
    /// </remarks>
    [Fact]
    public async Task Ein_Fund_traegt_kein_Sichtbarkeitsfeld()
    {
        var subject = Guid.CreateVersion7();
        await Write(subject, ["Reepschlagen"]);

        var term = await Intern($"/internal/profiles/{subject}");

        term.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo(
            "subject_id", "headline", "bio", "location", "remote_ok", "skills",
            // ADR-0041: die STUFE reist, nicht eine Kilometerzahl. Beide sind
            // nullbar und heissen dann „nichts gesagt".
            "commute_km", "relocation");

        term.GetProperty("skills").EnumerateArray()
            .Select(entry => entry.GetString()).Should().Equal("Reepschlagen");
    }

    /// <summary>
    /// Sie gibt AUCH PersonProfile heraus, die niemand freigegeben hat — mit Absicht.
    /// </summary>
    /// <remarks>
    /// <para>Die unbequemste Zusage dieser Tür, und deshalb steht sie als
    /// eigener Test da. Die Release prüft scout-service, für die ganze Seite
    /// auf einmal. Wer diese Tür für etwas anderes benutzt, holt sich die
    /// Prüfung dazu — sonst zeigt er PersonProfile, die niemand freigegeben hat.</para>
    ///
    /// <para>Sie ist genau deshalb hinter dem geteilten Geheimnis und ohne
    /// Gateway-Route: von aussen ist sie nicht erreichbar.</para>
    /// </remarks>
    [Fact]
    public async Task Sie_prueft_den_Ledger_nicht_und_das_ist_die_Zusage()
    {
        var subject = Guid.CreateVersion7();
        await Write(subject, ["Rostschutz"]);

        var vorher = _tor.Fragen;

        var found = await Intern("/internal/profiles/search?skill=Rostschutz");

        found.GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("subject_id").GetGuid())
            .Should().Contain(subject, "niemand hat hier etwas freigegeben");

        _tor.Fragen.Should().Be(vorher, "diese Tür fragt den Ledger nicht");
    }

    /// <summary>Ein PersonProfile, das es nicht gibt, ist ein 404.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Profil_ist_ein_404()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        (await browser.GetAsync(new Uri($"/internal/profiles/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------

    private async Task Write(Guid subject, string[] skills)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", Tokenform.Person(subject));

        var response = await browser.PutAsJsonAsync(
            new Uri("/profiles/me", UriKind.Relative),
            new
            {
                headline = "Entwicklerin",
                bio = "",
                location = "Berlin",
                remote_ok = true,
                skills = skills
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<JsonElement> Intern(string path)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        var response = await browser.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Json(response);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
