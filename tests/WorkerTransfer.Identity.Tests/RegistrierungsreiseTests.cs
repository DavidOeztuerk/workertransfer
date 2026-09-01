using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Records what went out — and whether it could have gone out too early.
/// </summary>
/// <remarks>
/// At send time it asks a <em>separate</em> connection whether the token row is
/// visible. Inside the transaction it would not be, so a mail sent too early is
/// caught here rather than in a comment.
/// </remarks>
public sealed class MitschreibenderVersender(string verbindung) : IVersender
{
    private readonly List<AusgehendePost> _post = [];

    public IReadOnlyList<AusgehendePost> Post => _post;

    /// <summary>Whether every mail found its row already committed.</summary>
    public List<bool> ZeileWarSichtbar { get; } = [];

    public async Task SendeAsync(
        AusgehendePost post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        await using var eigene = new NpgsqlConnection(verbindung);
        await eigene.OpenAsync(cancellationToken);
        await using var befehl = eigene.CreateCommand();
        befehl.CommandText = "SELECT count(*) FROM users WHERE id = @wer";
        befehl.Parameters.AddWithValue("wer", post.Empfaenger.Value);

        ZeileWarSichtbar.Add((long)(await befehl.ExecuteScalarAsync(cancellationToken))! > 0);
        _post.Add(post);
    }
}

/// <summary>The whole way in: register, confirm, sign in.</summary>
[Collection(PostgresCollection.Name)]
public class RegistrierungsreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Passwort = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private MitschreibenderVersender _versand = null!;

    public async Task InitializeAsync()
    {
        _versand = new MitschreibenderVersender(postgres.ConnectionString);

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Singleton<IVersender>(_versand)));
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

    private static string NeueAdresse(string vorname) =>
        $"{vorname}-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

    private Task<HttpResponseMessage> Registriere(
        string email, string? firma = null, string passwort = Passwort) =>
        Browser().PostAsJsonAsync("/auth/register", new
        {
            email,
            password = passwort,
            display_name = "Anna",
            company_name = firma
        });

    private Task<HttpResponseMessage> Bestaetige(string token) =>
        Browser().PostAsJsonAsync("/auth/verify-email", new { token });

    private Task<HttpResponseMessage> Anmelden(string email) =>
        Browser().PostAsJsonAsync("/auth/login", new { email, password = Passwort });

    /// <summary>The token out of the last confirmation mail.</summary>
    private string LetzterLink() =>
        Regex.Match(
            _versand.Post.Last(p => p.Betreff.Contains("bestätige", StringComparison.Ordinal)).Text,
            @"token=(?<token>[A-Za-z0-9_-]+)",
            RegexOptions.None, TimeSpan.FromSeconds(5)).Groups["token"].Value;

    [Fact]
    public async Task Registrieren_bestaetigen_anmelden()
    {
        var email = NeueAdresse("anna");

        (await Registriere(email)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Unconfirmed is its own answer, so the interface can offer the mail
        // again instead of leaving somebody in a dead end.
        (await Anmelden(email)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Bestaetige(LetzterLink())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Anmelden(email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Sent inside the transaction, a confirmation link reaches an inbox before
    /// the row it points at exists — and a failed commit leaves somebody with a
    /// link to nothing.
    /// </summary>
    [Fact]
    public async Task Die_Mail_geht_erst_nach_dem_Commit_hinaus()
    {
        await Registriere(NeueAdresse("berta"));

        _versand.Post.Should().NotBeEmpty();
        _versand.ZeileWarSichtbar.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task Der_Bestaetigungslink_zeigt_auf_die_Adresse_die_der_Browser_sieht()
    {
        await Registriere(NeueAdresse("clara"));

        _versand.Post.Last().Text.Should().Contain("http://localhost:5173/verify?token=");
    }

    /// <summary>
    /// The same 201 either way. What differs is who gets told, and it is not
    /// the person asking.
    /// </summary>
    [Fact]
    public async Task Eine_zweite_Registrierung_warnt_den_Besitzer_und_verraet_nichts()
    {
        var email = NeueAdresse("dora");

        var erste = await Registriere(email);
        var zweite = await Registriere(email);

        zweite.StatusCode.Should().Be(erste.StatusCode);
        (await zweite.Content.ReadAsStringAsync())
            .Should().Be(await erste.Content.ReadAsStringAsync());

        _versand.Post.Last().Betreff.Should().Contain("Registrierungsversuch");
        _versand.Post.Last().An.Should().Be(email);
    }

    /// <summary>
    /// Otherwise any number of valid links stay in circulation, and the oldest —
    /// possibly the one that went to the wrong mailbox — keeps working.
    /// </summary>
    [Fact]
    public async Task Ein_erneut_gesendeter_Link_entwertet_den_alten()
    {
        var email = NeueAdresse("emil");
        await Registriere(email);
        var alter = LetzterLink();

        var erneut = await Browser().PostAsJsonAsync("/auth/resend-verification", new { email });
        erneut.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var neuer = LetzterLink();
        neuer.Should().NotBe(alter);

        (await Bestaetige(alter)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Bestaetige(neuer)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Always 202, and nothing sent. A different answer would be exactly the
    /// enumeration channel /auth/register is built to close.
    /// </summary>
    [Fact]
    public async Task Ein_erneutes_Senden_an_eine_unbekannte_Adresse_ist_auch_202()
    {
        var vorher = _versand.Post.Count;

        var antwort = await Browser().PostAsJsonAsync(
            "/auth/resend-verification", new { email = NeueAdresse("niemand") });

        antwort.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _versand.Post.Should().HaveCount(vorher);
    }

    [Fact]
    public async Task Zweimal_derselbe_Link_ist_kein_Fehler()
    {
        await Registriere(NeueAdresse("frieda"));
        var link = LetzterLink();

        (await Bestaetige(link)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Bestaetige(link)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ein_erfundener_Link_ist_ungueltig()
    {
        (await Bestaetige("gibtesnicht")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ein_zu_kurzes_Passwort_wird_abgewiesen()
    {
        var antwort = await Registriere(NeueAdresse("greta"), passwort: "kurz");

        antwort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ein_Unternehmen_entsteht_bei_der_Bestaetigung_und_nicht_frueher()
    {
        var email = NeueAdresse("chefin");

        await Registriere(email, firma: "Beispiel GmbH");

        await using (var verbindung = new NpgsqlConnection(postgres.ConnectionString))
        {
            await verbindung.OpenAsync();
            await using var befehl = verbindung.CreateCommand();
            befehl.CommandText = "SELECT count(*) FROM tenants WHERE domain = @domain";
            befehl.Parameters.AddWithValue("domain", email.Split('@')[1]);
            ((long)(await befehl.ExecuteScalarAsync())!).Should().Be(
                0, "vor der Bestaetigung ist es nur eine Absicht");
        }

        var bestaetigt = await Bestaetige(LetzterLink());

        bestaetigt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await bestaetigt.Content.ReadAsStringAsync()).Should().Contain("Beispiel GmbH");
    }

    /// <summary>
    /// A state that did not exist before: confirmed account, refused
    /// company — and the answer says so instead of showing "all good".
    /// </summary>
    [Fact]
    public async Task Eine_beanspruchte_Domain_bestaetigt_das_Konto_trotzdem()
    {
        var domain = $"firma-{Guid.NewGuid():N}.example";
        var erste = $"chefin-{Guid.NewGuid():N}@{domain}";
        var zweite = $"kollege-{Guid.NewGuid():N}@{domain}";

        await Registriere(erste, firma: "Beispiel GmbH");
        await Bestaetige(LetzterLink());

        await Registriere(zweite, firma: "Zweite GmbH");
        var antwort = await Bestaetige(LetzterLink());

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        var koerper = await antwort.Content.ReadAsStringAsync();
        koerper.Should().Contain("domain_already_claimed");
        koerper.Should().NotContain("Zweite GmbH");

        // Und das Konto traegt: die Bestaetigung ist unumkehrbar richtig.
        (await Anmelden(zweite)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Eine_Freemail_Adresse_kann_kein_Unternehmen_beanspruchen()
    {
        var antwort = await Registriere(
            $"chefin-{Guid.NewGuid():N}@gmail.com", firma: "Beispiel GmbH");

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// The ordinary user of a transfer market is a person with no company
    /// at all. The blocklist applies at exactly one place.
    /// </summary>
    [Fact]
    public async Task Als_Privatperson_ist_eine_Freemail_Adresse_willkommen()
    {
        var antwort = await Registriere($"anna-{Guid.NewGuid():N}@gmail.com");

        antwort.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
