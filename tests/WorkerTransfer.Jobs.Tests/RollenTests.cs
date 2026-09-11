using System.Net;
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

namespace WorkerTransfer.Jobs.Tests;

/// <summary>
/// „admin" ist mehr als ein Wort in einer Tabelle — an den zwei Stellen, an
/// denen eine Anzeige das Unternehmen nach aussen vertritt.
/// </summary>
/// <remarks>
/// <para>Bis PBI-2 gab es im ganzen Baum <strong>keine einzige</strong>
/// Rechtepruefung ausserhalb von identity-service: die Navigation versteckte
/// Firmeneintraege, und der Server antwortete 403 nur dort, wo jemand daran
/// gedacht hatte. Verstecken ist keine Zugriffskontrolle.</para>
///
/// <para><strong>Jede Reihe hier hat ihre eigene Gegenprobe.</strong> Ein
/// Endpunkt, der IMMER 403 antwortet, ist von einem richtig geschuetzten nicht
/// zu unterscheiden — also steht neben jedem „member bekommt 403" auch
/// „admin bekommt 200", in derselben Reihe und am selben Vorgang.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RollenTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Rollenprobe _rollen = new();

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
                dienste.Replace(ServiceDescriptor.Scoped<IFirmenrollen>(_ => _rollen));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid firma, Guid? wer = null)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Firma(wer ?? Guid.CreateVersion7(), firma));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static async Task<Guid> Entwurf(HttpClient browser)
    {
        var angelegt = await browser.PostAsJsonAsync("/jobs", new
        {
            title = "Metallbauerin",
            description = "Wir bauen Gelaender.",
            location = "Berlin",
            remote_mode = "onsite",
            employment_type = "full_time",
            skills = new[] { "MIG/MAG" }
        });

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(angelegt)).GetProperty("id").GetGuid();
    }

    /// <summary>Ein <c>member</c> stellt keine Anzeige hin.</summary>
    [Fact]
    public async Task Nur_ein_Administrator_veroeffentlicht()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);
        var id = await Entwurf(browser);

        _rollen.Antwort = Firmenrolle.Mitglied;
        var alsMitglied = await browser.PostAsync($"/jobs/{id}/publish", null);

        alsMitglied.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "eine veroeffentlichte Anzeige vertritt das ganze Unternehmen");

        _rollen.Antwort = Firmenrolle.Admin;
        var alsAdmin = await browser.PostAsync($"/jobs/{id}/publish", null);

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Und schliesst keine — das ist die Handlung aus der Nutzergeschichte.
    /// </summary>
    /// <remarks>
    /// „Ein <c>member</c> kann meine Stellen nicht loeschen" heisst in dieser
    /// Domaene genau das: geloescht wird eine Anzeige nie, sie wird
    /// geschlossen. Und mit ihr verschwindet die Moeglichkeit, sich zu bewerben.
    /// </remarks>
    [Fact]
    public async Task Nur_ein_Administrator_schliesst()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);
        var id = await Entwurf(browser);

        (await browser.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _rollen.Antwort = Firmenrolle.Mitglied;
        var alsMitglied = await browser.PostAsync($"/jobs/{id}/close", null);

        alsMitglied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _rollen.Antwort = Firmenrolle.Admin;
        var alsAdmin = await browser.PostAsync($"/jobs/{id}/close", null);

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Ein <c>member</c> schreibt und aendert Anzeigen — sonst waere die
    /// Einladung eine Zuschauerkarte.
    /// </summary>
    /// <remarks>
    /// Die Haelfte, die leicht verlorengeht: ein zu enges Recht sieht aus wie
    /// Sicherheit und fuehrt dazu, dass jemand einen zweiten Admin anlegt, um
    /// arbeiten zu koennen — und dann ist „admin" wieder ein Wort in einer
    /// Tabelle.
    /// </remarks>
    [Fact]
    public async Task Ein_Mitglied_schreibt_und_aendert_eine_Anzeige()
    {
        _rollen.Antwort = Firmenrolle.Mitglied;

        var browser = AlsFirma(Guid.CreateVersion7());
        var id = await Entwurf(browser);

        var geaendert = await browser.PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Metallbauer",
            description = "Wir bauen Gelaender und Treppen.",
            location = "Berlin",
            remote_mode = "onsite",
            employment_type = "full_time",
            skills = new[] { "MIG/MAG", "WIG" }
        });

        geaendert.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Gefragt wird nach der Firma aus dem TOKEN und dem Menschen aus dem
    /// <c>sub</c> — nicht nach irgendetwas aus der Anfrage.
    /// </summary>
    /// <remarks>
    /// Ohne diese Reihe koennte die Attrappe „admin" sagen, waehrend der
    /// Aufrufer eine fremde Firma uebergibt, und alles saehe gruen aus. Der
    /// Mandant im Token kam nie aus einer Eingabe: ihn vergibt
    /// <c>POST /auth/company/{id}</c>, nachdem der Server die Mitgliedschaft
    /// geprueft hat (ADR-0018).
    /// </remarks>
    [Fact]
    public async Task Gefragt_wird_nach_dem_Mandanten_aus_dem_Token()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var browser = AlsFirma(firma, wer);
        var id = await Entwurf(browser);

        await browser.PostAsync($"/jobs/{id}/publish", null);

        _rollen.Zuletzt.Should().Be((wer, firma));
    }

    /// <summary>
    /// Schweigt die Rollenauskunft, ist das kein Nein.
    /// </summary>
    /// <remarks>
    /// 503 heisst „hat nicht geantwortet", 403 heisst „du darfst nicht" —
    /// dieselbe Unterscheidung, die dieser Baum beim Einwilligungs-Ledger
    /// trifft. Ein Ausfall von identity-service als 403 zu beantworten waere
    /// eine Luege, die nach einer Entscheidung aussieht: der Mensch davor liest
    /// „dir wurde das Recht genommen", und niemand sucht nach einem Ausfall.
    /// </remarks>
    [Fact]
    public async Task Eine_schweigende_Rollenauskunft_ist_kein_Nein()
    {
        var browser = AlsFirma(Guid.CreateVersion7());
        var id = await Entwurf(browser);

        _rollen.Schweigt = true;
        var antwort = await browser.PostAsync($"/jobs/{id}/publish", null);

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var problem = await Json(antwort);
        problem.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Ohne Token 401, als Person ohne Firma 403 — die Richtlinie aendert
    /// daran nichts.
    /// </summary>
    /// <remarks>
    /// Die beiden Spalten der Routenkarte, die es vor PBI-2 schon gab. Sie
    /// stehen hier, weil eine neue Richtlinie leicht aus einem 403 ein 401
    /// macht: das Geruest fordert erst Anmeldung an und fragt dann nach dem
    /// Recht.
    /// </remarks>
    [Fact]
    public async Task Ohne_Token_und_ohne_Firma_bleibt_es_wie_es_war()
    {
        var id = await Entwurf(AlsFirma(Guid.CreateVersion7()));

        var ohne = await _dienst.CreateClient().PostAsync($"/jobs/{id}/publish", null);
        ohne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = _dienst.CreateClient();
        person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Person(Guid.CreateVersion7()));

        (await person.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
