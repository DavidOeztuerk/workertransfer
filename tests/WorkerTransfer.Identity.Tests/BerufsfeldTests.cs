using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Das Berufsfeld ordnet, was angeboten wird — und sonst nichts (ADR-0039).</summary>
[Collection(PostgresCollection.Name)]
public class BerufsfeldTests(Postgres postgres) : IAsyncLifetime
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

    /// <summary>Die elf Etiketten als MENGE, damit ein zwölftes auffällt.</summary>
    [Fact]
    public void Die_Liste_ist_geschlossen_und_vollstaendig() =>
        Berufsfeldwahl.Etiketten.Should().BeEquivalentTo(
        [
            "handwerk", "industrie_technik", "bau", "gesundheit_pflege",
            "logistik_verkehr", "gastronomie_hotel", "handel_verkauf",
            "buero_verwaltung", "it_software", "bildung_soziales", "sonstiges"
        ]);

    /// <summary>
    /// Jedes Etikett kommt als es selbst zurück — beide Richtungen sind von
    /// Hand geschrieben.
    /// </summary>
    [Fact]
    public void Jedes_Etikett_ueberlebt_den_Hin_und_Rueckweg()
    {
        foreach (var etikett in Berufsfeldwahl.Etiketten)
        {
            Berufsfeldwahl.Etikett(Berufsfeldwahl.Aus(etikett)).Should().Be(etikett);
        }
    }

    /// <summary>„Sonstiges" ist eine Wahl, „nichts" ist keine.</summary>
    [Fact]
    public void Sonstiges_ist_nicht_dasselbe_wie_nichts()
    {
        Berufsfeldwahl.Aus("sonstiges").Should().Be(Berufsfeld.Sonstiges);
        Berufsfeldwahl.Aus(null).Should().BeNull();
        Berufsfeldwahl.Aus("  ").Should().BeNull();
    }

    /// <summary>Leer ist ein zulässiger Wert, Unsinn nicht.</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("handwerk", true)]
    [InlineData("HANDWERK", true)]
    [InlineData("industrietechnik", false)]
    [InlineData("metallbauer", false)]
    public void Kennen_sagt_was_gespeichert_werden_darf(string? etikett, bool erwartet) =>
        Berufsfeldwahl.Kennen(etikett).Should().Be(erwartet);

    /// <summary>
    /// Wer nichts wählt, bekommt <c>null</c>. Nichts wird geraten — eine
    /// GitHub-Verbindung macht niemanden zu <c>it_software</c> (ADR-0022 §2).
    /// </summary>
    [Fact]
    public async Task Ohne_Wahl_steht_nichts_da()
    {
        var browser = await AngemeldetesKonto();

        var sitzung = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");

        sitzung!.User!.OccupationalField.Should().BeNull();
    }

    /// <summary>Was bei der Registrierung gewählt wurde, steht danach da.</summary>
    [Fact]
    public async Task Die_Wahl_bei_der_Registrierung_haelt()
    {
        var browser = await AngemeldetesKonto(feld: "handwerk");

        var sitzung = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");

        sitzung!.User!.OccupationalField.Should().Be("handwerk");
    }

    /// <summary>
    /// Die Wahl lässt sich nachholen und zurücknehmen — <c>null</c> heisst
    /// entfernen, nicht „unverändert".
    /// </summary>
    [Fact]
    public async Task Nachtragen_und_zuruecknehmen_wirken_beide()
    {
        var browser = await AngemeldetesKonto();

        var gesetzt = await browser.PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = "gesundheit_pflege" });
        gesetzt.StatusCode.Should().Be(HttpStatusCode.OK);

        var nachher = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");
        nachher!.User!.OccupationalField.Should().Be("gesundheit_pflege");

        var geleert = await browser.PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = (string?)null });
        geleert.StatusCode.Should().Be(HttpStatusCode.OK);

        var zurueck = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");
        zurueck!.User!.OccupationalField.Should().BeNull();
    }

    /// <summary>Ein unbekanntes Etikett wird abgesagt, nicht still verworfen.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Feld_wird_abgesagt()
    {
        var browser = await AngemeldetesKonto(feld: "handwerk");

        var antwort = await browser.PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = "metallbauer" });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Eine abgesagte Änderung darf nichts angerichtet haben.
        var sitzung = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");
        sitzung!.User!.OccupationalField.Should().Be("handwerk");
    }

    /// <summary>Auch die Registrierung sagt ab, statt die Angabe wegzuwerfen.</summary>
    [Fact]
    public async Task Eine_Registrierung_mit_Unsinn_wird_abgesagt()
    {
        var antwort = await _dienst.CreateClient().PostAsJsonAsync("/auth/register", new
        {
            email = NeueAdresse("unsinn"),
            password = Passwort,
            display_name = "Unsinn",
            occupational_field = "raumfahrt"
        });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ohne Anmeldung ändert niemand ein Berufsfeld.</summary>
    [Fact]
    public async Task Ohne_Anmeldung_geht_es_nicht()
    {
        var antwort = await _dienst.CreateClient().PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = "bau" });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string NeueAdresse(string wer) =>
        $"{wer}-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

    /// <summary>Registriert, bestätigt und meldet an.</summary>
    private async Task<HttpClient> AngemeldetesKonto(string? feld = null)
    {
        var adresse = NeueAdresse("anna");
        var browser = _dienst.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = adresse,
            password = Passwort,
            display_name = "Anna",
            occupational_field = feld
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new
        {
            email = adresse, password = Passwort
        });

        return browser;
    }

    private sealed record SitzungsAntwort(BenutzerAntwort? User);

    private sealed record BenutzerAntwort(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("occupational_field")] string? OccupationalField);
}
