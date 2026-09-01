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

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Die Löschung — hier ohne jede Ausnahme.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschungTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeGitHub _github = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:github", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IGitHub>(_ => _github));
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor, Probeledger>());
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Die Verknüpfung fällt — und mit ihr das Personendatum.
    /// </summary>
    /// <remarks>
    /// Der Login ist öffentlich; jeder kann ihn auf github.com nachschlagen.
    /// Das Personendatum ist nicht der Name, sondern die Verknüpfung „dieser
    /// Plattform-Mensch ist jener GitHub-Name".
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_die_Verknuepfung_mit()
    {
        var anna = Guid.CreateVersion7();
        await Verbinde(anna, "anna-dev");

        var antwort = await Loesche(anna);

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var kontext = postgres.Kontext();
        (await kontext.Verbindungen.AnyAsync(zeile => zeile.Id == anna)).Should().BeFalse();
    }

    /// <summary>Die Löschung eines Menschen fasst niemand anderen an.</summary>
    [Fact]
    public async Task Die_Loeschung_trifft_nur_diesen_Menschen()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();
        await Verbinde(anna, "anna-dev");
        await Verbinde(bea, "bea-dev");

        await Loesche(anna);

        await using var kontext = postgres.Kontext();
        (await kontext.Verbindungen.AnyAsync(zeile => zeile.Id == bea)).Should().BeTrue();
    }

    /// <summary>Ohne das Geheimnis passiert nichts.</summary>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_geloescht()
    {
        var anna = Guid.CreateVersion7();
        await Verbinde(anna, "anna-dev");

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

        var antwort = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var kontext = postgres.Kontext();
        (await kontext.Verbindungen.AnyAsync(zeile => zeile.Id == anna)).Should().BeTrue();
    }

    /// <summary>Ein zweiter Aufruf ist kein Fehlschlag.</summary>
    [Fact]
    public async Task Ein_zweites_Mal_loeschen_ist_wieder_2xx()
    {
        var anna = Guid.CreateVersion7();

        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> Loesche(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
        return browser.PostAsJsonAsync("/erasure", new { user_id = wer });
    }

    private Task<HttpResponseMessage> Verbinde(Guid wer, string login)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(wer));

        return browser.PostAsJsonAsync("/github/me", new { login });
    }
}
