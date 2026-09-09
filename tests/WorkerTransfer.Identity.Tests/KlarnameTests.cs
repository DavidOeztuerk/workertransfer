using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Vorname, Nachname, Anschrift — ADR-0038.</summary>
[Collection(PostgresCollection.Name)]
public class KlarnameTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Passwort = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Postmitschrift _post = new();

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("Notify:Geheimnis", "melde-geheimnis");
            host.UseSetting(Geheimnisspeicher.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Singleton<IVersender>(_post)));
        });

        using var bereich = _dienst.Services.CreateScope();
        await bereich.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> Angemeldet(string? vorname = null, string? nachname = null)
    {
        var adresse = $"anna-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = adresse,
            password = Passwort,
            display_name = "Anna",
            given_name = vorname,
            family_name = nachname
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = adresse, password = Passwort });
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Registrierung_ohne_Vornamen_ist_in_Ordnung()
    {
        var browser = await Angemeldet();
        var sitzung = await Json(await browser.GetAsync("/auth/session"));

        sitzung.GetProperty("user").GetProperty("display_name").GetString().Should().Be("Anna");
        sitzung.GetProperty("user").GetProperty("given_name").ValueKind
            .Should().Be(JsonValueKind.Null);
        sitzung.GetProperty("user").TryGetProperty("line1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Registrierung_nimmt_Klarnamen_in_snake_case()
    {
        var browser = await Angemeldet("Anna", "Beispiel");
        var sitzung = await Json(await browser.GetAsync("/auth/session"));
        var wer = sitzung.GetProperty("user");

        wer.GetProperty("given_name").GetString().Should().Be("Anna");
        wer.GetProperty("family_name").GetString().Should().Be("Beispiel");
        wer.GetProperty("display_name").GetString().Should().Be("Anna");
    }

    [Fact]
    public async Task Klarname_laesst_sich_setzen_und_leeren()
    {
        var browser = await Angemeldet();

        (await browser.PutAsJsonAsync("/account/name", new
        {
            given_name = "Bea",
            family_name = "Muster"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var sitzung = await Json(await browser.GetAsync("/auth/session"));
        sitzung.GetProperty("user").GetProperty("given_name").GetString().Should().Be("Bea");

        (await browser.PutAsJsonAsync("/account/name", new
        {
            given_name = "",
            family_name = ""
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        sitzung = await Json(await browser.GetAsync("/me"));
        sitzung.GetProperty("given_name").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Anschrift_ist_leer_bis_jemand_sie_setzt_und_steht_nicht_in_der_Session()
    {
        var browser = await Angemeldet();

        var leer = await Json(await browser.GetAsync("/account/address"));
        leer.GetProperty("line1").GetString().Should().Be("");
        leer.GetProperty("country").GetString().Should().Be("DE");

        (await browser.PutAsJsonAsync("/account/address", new
        {
            line1 = "Musterstraße 1",
            line2 = "",
            postal_code = "10115",
            city = "Berlin",
            country = "de",
            phone = "+49 30 123"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var anschrift = await Json(await browser.GetAsync("/account/address"));
        anschrift.GetProperty("line1").GetString().Should().Be("Musterstraße 1");
        anschrift.GetProperty("country").GetString().Should().Be("DE");
        anschrift.GetProperty("phone").GetString().Should().Be("+49 30 123");

        var sitzung = await Json(await browser.GetAsync("/auth/session"));
        sitzung.GetProperty("user").TryGetProperty("line1", out _).Should().BeFalse();
        sitzung.GetProperty("user").TryGetProperty("phone", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Ohne_Token_bleibt_die_Anschrift_zu()
    {
        (await Browser().GetAsync("/account/address"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Browser().PutAsJsonAsync("/account/name", new { given_name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
