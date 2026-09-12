using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.ServiceDefaults.Rollen;

namespace WorkerTransfer.Companies.Tests;

/// <summary>
/// Das Schaufenster schreibt, wer für das Unternehmen sprechen darf.
/// </summary>
/// <remarks>
/// Ein Arbeitgeberprofil trägt Name, Beschreibung, Anschrift und das Kürzel,
/// unter dem die Karriereseite <strong>ohne Anmeldung</strong> erreichbar ist.
/// Wer es ändert, ändert, wie das Unternehmen in der Welt erscheint — und zwar
/// für alle anderen mit.
/// <para>
/// Lesen bleibt jedem Mitglied: es steht ohnehin im Netz.
/// </para>
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
            host.UseSetting("ConnectionStrings:companies", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IFirmenrollen>(_ => _rollen)));
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

    private static Task<HttpResponseMessage> Schreibe(HttpClient browser, string name) =>
        browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = name,
            about = "Wir bauen Gelaender.",
            website = "https://muster.example",
            locations = new[] { "Berlin" },
            benefits = new[] { "Werkzeug gestellt" }
        });

    /// <summary>Ein <c>member</c> schreibt das Schaufenster nicht.</summary>
    [Fact]
    public async Task Nur_ein_Administrator_schreibt_das_Arbeitgeberprofil()
    {
        var browser = AlsFirma(Guid.CreateVersion7());

        _rollen.Antwort = Firmenrolle.Mitglied;
        var alsMitglied = await Schreibe(browser, "Muster GmbH");

        alsMitglied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Die Gegenprobe in derselben Reihe: ein Endpunkt, der immer 403 gibt,
        // saehe von hier aus genauso aus.
        _rollen.Antwort = Firmenrolle.Admin;
        var alsAdmin = await Schreibe(browser, "Muster GmbH");

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Lesen darf jedes Mitglied — sonst waere die Einladung eine
    /// Zuschauerkarte.
    /// </summary>
    [Fact]
    public async Task Ein_Mitglied_liest_das_eigene_Schaufenster()
    {
        var firma = Guid.CreateVersion7();

        _rollen.Antwort = Firmenrolle.Admin;
        (await Schreibe(AlsFirma(firma), "Muster GmbH"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _rollen.Antwort = Firmenrolle.Mitglied;
        var gelesen = await AlsFirma(firma).GetAsync("/companies/me/profile");

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);

        var rumpf = JsonDocument.Parse(await gelesen.Content.ReadAsStringAsync()).RootElement;
        rumpf.GetProperty("display_name").GetString().Should().Be("Muster GmbH");
    }

    /// <summary>
    /// Gefragt wird nach der Firma aus dem TOKEN, nicht nach etwas aus der
    /// Anfrage.
    /// </summary>
    [Fact]
    public async Task Gefragt_wird_nach_dem_Mandanten_aus_dem_Token()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();

        await Schreibe(AlsFirma(firma, wer), "Muster GmbH");

        _rollen.Zuletzt.Should().Be((wer, firma));
    }

    /// <summary>Schweigt die Rollenauskunft, ist das kein Nein.</summary>
    [Fact]
    public async Task Eine_schweigende_Rollenauskunft_ist_kein_Nein()
    {
        _rollen.Schweigt = true;

        var antwort = await Schreibe(AlsFirma(Guid.CreateVersion7()), "Muster GmbH");

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Ohne Token 401, als Person ohne Firma 403 — die Richtlinie aendert
    /// daran nichts.
    /// </summary>
    [Fact]
    public async Task Ohne_Token_und_ohne_Firma_bleibt_es_wie_es_war()
    {
        var ohne = await Schreibe(_dienst.CreateClient(), "Muster GmbH");
        ohne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = _dienst.CreateClient();
        person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Person(Guid.CreateVersion7()));

        (await Schreibe(person, "Muster GmbH")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
