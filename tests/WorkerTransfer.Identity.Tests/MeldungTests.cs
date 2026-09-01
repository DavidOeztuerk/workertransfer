using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Ein Versender, der nur mitschreibt.</summary>
public sealed class Postmitschrift : IVersender
{
    private readonly List<AusgehendePost> _post = [];

    /// <summary>Was hinausgegangen wäre.</summary>
    public IReadOnlyList<AusgehendePost> Post => _post;

    /// <inheritdoc />
    public Task SendeAsync(AusgehendePost post, CancellationToken cancellationToken = default)
    {
        _post.Add(post);
        return Task.CompletedTask;
    }
}

/// <summary><c>POST /internal/notify</c> — der einzige Weg zu einer Adresse.</summary>
/// <remarks>
/// notification-service entscheidet, <em>ob</em> etwas hinausgeht; hier steht,
/// <em>an wen</em>. Über die Grenze geht nur eine Kennung — diese Reihe hält
/// fest, dass sich daraus kein Text und keine Auskunft ableiten lässt.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class MeldungTests(Postgres postgres) : IAsyncLifetime
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

    private Task<HttpResponseMessage> Melde(Guid wer, string geheimnis = Meldegeheimnis)
    {
        var browser = Browser();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", geheimnis);
        return browser.PostAsJsonAsync("/internal/notify", new { user_id = wer });
    }

    /// <summary>Legt ein bestätigtes Konto an und gibt seine Kennung zurück.</summary>
    private async Task<Guid> BestaetigtesKonto()
    {
        var adresse = $"anna-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

        await Browser().PostAsJsonAsync("/auth/register", new
        {
            email = adresse, password = Passwort, display_name = "Anna"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;

        await Browser().PostAsJsonAsync("/auth/verify-email", new { token });

        return _post.Post[^1].Empfaenger.Value;
    }

    /// <summary>Ein unbestätigtes Konto, dessen Adresse noch nicht erwiesen ist.</summary>
    private async Task<Guid> UnbestaetigtesKonto()
    {
        var adresse = $"bea-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

        await Browser().PostAsJsonAsync("/auth/register", new
        {
            email = adresse, password = Passwort, display_name = "Bea"
        });

        return _post.Post[^1].Empfaenger.Value;
    }

    /// <summary>Der gewöhnliche Weg: eine Kennung hinein, eine Mail hinaus.</summary>
    [Fact]
    public async Task Eine_Kennung_genuegt_fuer_die_eine_Mail()
    {
        var anna = await BestaetigtesKonto();
        var vorher = _post.Post.Count;

        var antwort = await Melde(anna);

        antwort.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _post.Post.Should().HaveCount(vorher + 1);
        _post.Post[^1].Empfaenger.Value.Should().Be(anna);
    }

    /// <summary>
    /// Für jede Art derselbe Betreff — und die Art steht gar nicht im Rumpf.
    /// </summary>
    /// <remarks>
    /// Eine Mail landet in einem Postfach, das das des Arbeitgebers sein kann.
    /// Eine Zeile wie „Acme GmbH möchte deinen Marktstatus sehen" wäre genau die
    /// Auskunft, gegen die diese Plattform gebaut ist.
    /// </remarks>
    [Fact]
    public async Task Die_Mail_sagt_nicht_worum_es_geht()
    {
        var anna = await BestaetigtesKonto();

        await Melde(anna);

        var mail = _post.Post[^1];

        mail.Betreff.Should().Be("Neuigkeiten auf WorkerTransfer");
        mail.Text.Should().NotContainAny(
            "Lebenslauf", "Marktstatus", "Bewerbung", "Transfer", "Unternehmen");
        mail.Text.Should().Contain("Melde dich an");
    }

    /// <summary>
    /// Ein unbestätigtes Konto bekommt keine Post über Vorgänge — und der
    /// Aufrufer erfährt davon nichts.
    /// </summary>
    [Fact]
    public async Task Ein_unbestaetigtes_Konto_bekommt_keine_Post()
    {
        var bea = await UnbestaetigtesKonto();
        var vorher = _post.Post.Count;

        var antwort = await Melde(bea);

        antwort.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _post.Post.Should().HaveCount(vorher);
    }

    /// <summary>
    /// Immer 202 — sonst wäre der Endpunkt ein Orakel über die Mitgliedschaft
    /// auf dieser Plattform.
    /// </summary>
    [Fact]
    public async Task Bekannt_und_unbekannt_antworten_gleich()
    {
        var anna = await BestaetigtesKonto();

        var bekannt = await Melde(anna);
        var unbekannt = await Melde(Guid.CreateVersion7());

        unbekannt.StatusCode.Should().Be(bekannt.StatusCode);
        (await unbekannt.Content.ReadAsStringAsync())
            .Should().Be(await bekannt.Content.ReadAsStringAsync());
    }

    /// <summary>Ohne das Geheimnis ist es 404, nicht 401.</summary>
    /// <remarks>
    /// Ein 401 bestätigt, dass es den Endpunkt gibt.
    /// </remarks>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_ist_es_404()
    {
        var anna = await BestaetigtesKonto();
        var vorher = _post.Post.Count;

        var antwort = await Melde(anna, "geraten");

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _post.Post.Should().HaveCount(vorher);
    }
}
