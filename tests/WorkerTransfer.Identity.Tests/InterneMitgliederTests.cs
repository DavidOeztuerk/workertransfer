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

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// <c>GET /internal/companies/{tenantId}/members</c> — der Draht, über den
/// applications-service die Empfänger einer Bewerbungsmail auflöst.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InterneMitgliederTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Meldegeheimnis = "melde-geheimnis";
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
            host.UseSetting("Notify:Geheimnis", Meldegeheimnis);
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

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private async Task<(Guid Firma, Guid Wer)> FirmaMitMitglied()
    {
        var domain = $"firma-{Guid.NewGuid():N}.example";
        var adresse = $"anna-{Guid.NewGuid():N}@{domain}";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = adresse,
            password = Passwort,
            display_name = "Anna",
            company_name = "Beispiel GmbH"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = adresse, password = Passwort });

        var ich = await Json(await browser.GetAsync("/me"));
        var meine = await Json(await browser.GetAsync("/me/companies"));

        return (Guid.Parse(meine[0].GetProperty("id").GetString()!),
            Guid.Parse(ich.GetProperty("user_id").GetString()!));
    }

    private HttpClient MitGeheimnis(string geheimnis = Meldegeheimnis)
    {
        var browser = Browser();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", geheimnis);
        return browser;
    }

    /// <summary>Ohne Geheimnis ist der Endpunkt unsichtbar.</summary>
    [Fact]
    public async Task Ohne_Geheimnis_ist_es_404()
    {
        var (firma, _) = await FirmaMitMitglied();

        var antwort = await Browser().GetAsync($"/internal/companies/{firma}/members");

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ein geratenes Geheimnis hebt die Tür nicht.</summary>
    [Fact]
    public async Task Ein_geratenes_Geheimnis_ist_404()
    {
        var (firma, _) = await FirmaMitMitglied();

        var antwort = await MitGeheimnis("geraten")
            .GetAsync($"/internal/companies/{firma}/members");

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Mit dem Geheimnis kommt die Kennung — und sie reist als
    /// <c>subject_id</c>, nicht als <c>subjectId</c>.
    /// </summary>
    [Fact]
    public async Task Mit_Geheimnis_kommen_die_Mitglieder_als_snake_case()
    {
        var (firma, wer) = await FirmaMitMitglied();

        var antwort = await MitGeheimnis().GetAsync($"/internal/companies/{firma}/members");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var rumpf = await antwort.Content.ReadAsStringAsync();
        rumpf.Should().Contain("subject_id");
        rumpf.Should().NotContain("subjectId");
        rumpf.Should().NotContain("display_name");

        var gelesen = await Json(antwort);
        var mitglieder = gelesen.GetProperty("mitglieder");
        mitglieder.GetArrayLength().Should().Be(1);
        mitglieder[0].GetProperty("subject_id").GetGuid().Should().Be(wer);
    }

    /// <summary>
    /// Eine unbekannte Firma ist eine leere Liste, kein 404: sonst wäre der
    /// Endpunkt ein Orakel darüber, ob es diese Firma gibt.
    /// </summary>
    [Fact]
    public async Task Eine_unbekannte_Firma_ist_eine_leere_Liste()
    {
        var antwort = await MitGeheimnis()
            .GetAsync($"/internal/companies/{Guid.CreateVersion7()}/members");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("mitglieder").GetArrayLength().Should().Be(0);
    }
}
