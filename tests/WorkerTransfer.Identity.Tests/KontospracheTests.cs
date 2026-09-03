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

/// <summary>Die Sprache steht am Konto, nicht an der Anfrage.</summary>
/// <remarks>
/// Die Reihe hält drei Dinge fest, und alle drei sind der Grund für die Spalte:
/// die Bestätigungsmail folgt beim ersten Mal dem Kopf, die Wahl überschreibt
/// ihn dauerhaft, und eine Mail, die ohne Anfrage geschrieben wird, findet die
/// Wahl trotzdem.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class KontospracheTests(Postgres postgres) : IAsyncLifetime
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

    private HttpClient Browser(string? sprachkopf = null)
    {
        var browser = _dienst.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        if (sprachkopf is not null)
        {
            browser.DefaultRequestHeaders.Add("Accept-Language", sprachkopf);
        }

        return browser;
    }

    private static string NeueAdresse(string wer) =>
        $"{wer}-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

    /// <summary>Registriert, bestätigt und meldet an. Gibt den Browser zurück.</summary>
    private async Task<(HttpClient Browser, string Adresse)> AngemeldetesKonto(
        string? sprachkopf = null)
    {
        var adresse = NeueAdresse("anna");
        var browser = Browser(sprachkopf);

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = adresse, password = Passwort, display_name = "Anna"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new
        {
            email = adresse, password = Passwort
        });

        return (browser, adresse);
    }

    /// <summary>
    /// Beim ersten Mal gibt es nur den Kopf — und die Mail folgt ihm.
    /// </summary>
    /// <remarks>
    /// Die Bestätigungsmail geht raus, bevor irgendjemand eine Sprache wählen
    /// konnte. Ohne diesen Griff wäre die allererste Nachricht an ein
    /// französisches Konto deutsch.
    /// </remarks>
    [Theory]
    [InlineData("fr-CA,fr;q=0.9", "Merci de confirmer")]
    [InlineData("en-GB,en;q=0.9", "Please confirm")]
    [InlineData("de-DE,de;q=0.9", "Bitte bestätige")]
    public async Task Die_erste_Mail_folgt_dem_Kopf(string kopf, string erwartet)
    {
        await Browser(kopf).PostAsJsonAsync("/auth/register", new
        {
            email = NeueAdresse("erst"), password = Passwort, display_name = "Erst"
        });

        _post.Post[^1].Betreff.Should().Contain(erwartet);
    }

    /// <summary>
    /// Eine Sprache, die es hier nicht gibt, führt zur Quellsprache — nicht zu
    /// einem Fehlschlag.
    /// </summary>
    /// <remarks>
    /// Eine Registrierung darf nicht daran scheitern, dass ein Browser
    /// Isländisch ansagt. Und der Kopf nennt oft mehrere: steht eine Sprache,
    /// die wir haben, an zweiter Stelle, muss sie gewinnen — sonst hätte die
    /// erste unbekannte Angabe die Wirkung einer Wahl.
    /// </remarks>
    [Theory]
    [InlineData("is-IS", "Bitte bestätige")]
    [InlineData("is-IS,fr;q=0.8", "Merci de confirmer")]
    [InlineData("*", "Bitte bestätige")]
    public async Task Unbekanntes_im_Kopf_faellt_auf_die_Quellsprache(
        string kopf, string erwartet)
    {
        await Browser(kopf).PostAsJsonAsync("/auth/register", new
        {
            email = NeueAdresse("fremd"), password = Passwort, display_name = "Fremd"
        });

        _post.Post[^1].Betreff.Should().Contain(erwartet);
    }

    /// <summary>Die Wahl steht am Konto und schlägt den Kopf.</summary>
    /// <remarks>
    /// Der entscheidende Fall: der Browser sagt weiterhin Deutsch, das Konto
    /// sagt Französisch. Würde der Kopf gelesen, wäre die Wahl folgenlos —
    /// und niemand erführe davon.
    /// </remarks>
    [Fact]
    public async Task Die_Wahl_schlaegt_den_Kopf()
    {
        var (browser, _) = await AngemeldetesKonto("de-DE");

        var gesetzt = await browser.PutAsJsonAsync(
            "/account/language", new { language = "fr" });
        gesetzt.StatusCode.Should().Be(HttpStatusCode.OK);

        // Derselbe Browser, weiterhin mit deutschem Kopf.
        await browser.PostAsJsonAsync("/auth/resend-verification", new
        {
            email = "unbekannt@example.org"
        });

        var sitzung = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");
        sitzung!.User!.Language.Should().Be("fr");
    }

    /// <summary>
    /// Eine Mail ohne Anfrage findet die Wahl trotzdem — das ist der Grund für
    /// die Spalte.
    /// </summary>
    /// <remarks>
    /// <c>POST /internal/notify</c> kommt von notification-service und trägt
    /// keinen Browserkopf. Stünde die Sprache an der Anfrage, wäre diese Mail
    /// immer deutsch.
    /// </remarks>
    [Fact]
    public async Task Eine_Mail_ohne_Anfrage_findet_die_Wahl()
    {
        var (browser, _) = await AngemeldetesKonto("de-DE");
        await browser.PutAsJsonAsync("/account/language", new { language = "fr" });

        var sitzung = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");
        var wer = Guid.Parse(sitzung!.User!.UserId);

        var melder = Browser();
        melder.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);
        var gemeldet = await melder.PostAsJsonAsync(
            "/internal/notify", new { user_id = wer });

        gemeldet.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _post.Post[^1].Betreff.Should().Be("Du nouveau sur WorkerTransfer");
    }

    /// <summary>
    /// Eine Sprache ohne Texte wird abgelehnt, nicht stillschweigend ersetzt.
    /// </summary>
    /// <remarks>
    /// Der Gegenprobe wegen wichtig: <c>Sprachwahl.Aus</c> beantwortet
    /// Unbekanntes mit Deutsch, und genau das wäre hier falsch. Eine Wahl, die
    /// nicht wirkt und nichts sagt, sieht für den Menschen aus wie ein Fehler
    /// der Oberfläche.
    /// </remarks>
    [Fact]
    public async Task Eine_Sprache_ohne_Texte_wird_abgelehnt()
    {
        var (browser, _) = await AngemeldetesKonto();

        var antwort = await browser.PutAsJsonAsync(
            "/account/language", new { language = "is" });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var sitzung = await browser.GetFromJsonAsync<SitzungsAntwort>("/auth/session");
        sitzung!.User!.Language.Should().Be("de");
    }

    /// <summary>Ohne Anmeldung ändert niemand eine Sprache.</summary>
    /// <remarks>
    /// Es gibt keine Kennung im Rumpf, und das ist der eigentliche Schutz: eine
    /// wäre das Einzige zwischen einem Aufrufer und einem fremden Konto.
    /// </remarks>
    [Fact]
    public async Task Ohne_Anmeldung_geht_es_nicht()
    {
        var antwort = await Browser().PutAsJsonAsync(
            "/account/language", new { language = "fr" });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Jede der drei Sprachen hat jeden der fünf Texte.</summary>
    /// <remarks>
    /// Derselbe Wächter wie im Frontend, und aus demselben Grund: eine fehlende
    /// Übersetzung fällt sonst erst auf, wenn eine Mail schon draussen ist.
    /// Geprüft wird auch, dass keiner der Texte die deutsche Fassung KOPIERT —
    /// eine Kopie sieht in der Zustellung aus wie eine Übersetzung.
    /// </remarks>
    [Fact]
    public void Jede_Sprache_hat_jeden_Text()
    {
        var deutsch = Texte(Kontosprache.De);

        foreach (var sprache in new[] { Kontosprache.En, Kontosprache.Fr })
        {
            var texte = Texte(sprache);

            texte.Should().HaveSameCount(deutsch);
            texte.Should().OnlyContain(
                text => !string.IsNullOrWhiteSpace(text.Betreff)
                        && !string.IsNullOrWhiteSpace(text.Text));

            for (var i = 0; i < texte.Count; i++)
            {
                texte[i].Betreff.Should().NotBe(
                    deutsch[i].Betreff,
                    $"{sprache} soll übersetzen, nicht kopieren");
            }
        }
    }

    private static IReadOnlyList<Mailtext> Texte(Kontosprache sprache) =>
    [
        Mailtexte.Bestaetigung(sprache, "http://x/verify?token=t"),
        Mailtexte.Doppelanmeldung(sprache),
        Mailtexte.Einladung(sprache, "Acme", "http://x/invitation?token=t"),
        Mailtexte.Loeschbestaetigung(sprache),
        Mailtexte.Neuigkeit(sprache, "http://x")
    ];

    private sealed record SitzungsAntwort(BenutzerAntwort? User);

    /// <summary>Die Antwort, wie sie WIRKLICH aussieht.</summary>
    /// <remarks>
    /// Die Namen stehen ausgeschrieben da, weil der Draht snake_case spricht und
    /// der Vergleich sonst still schiefgeht: <c>user_id</c> träfe kein
    /// <c>UserId</c>, das Feld bliebe null, und der Test fiele mit einer
    /// Meldung, die nichts über die Sache aussagt.
    /// </remarks>
    private sealed record BenutzerAntwort(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("tenant_id")] string? TenantId,
        [property: JsonPropertyName("language")] string Language);
}
