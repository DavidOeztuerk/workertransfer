using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Loeschung;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Tests;

/// <summary>What the test decides, shared across every scope.</summary>
/// <remarks>
/// Separate from the delivery itself because the delivery is scoped: it holds a
/// database context, and one kept past its scope is a disposed one. Caching the
/// wrapper across passes is exactly that mistake, and it shows up as
/// <c>ObjectDisposedException</c> in a warning nobody reads.
/// </remarks>
public sealed class Probesteuerung
{
    /// <summary>Recipients that refuse. Everything else goes through.</summary>
    public HashSet<string> Tot { get; } = new(StringComparer.Ordinal);

    /// <summary>What went out, in order.</summary>
    public List<string> Zugestellt { get; } = [];
}

/// <summary>A delivery that answers the way a test tells it to.</summary>
public sealed class Probeempfaenger(Probesteuerung steuerung, IZustellung echt) : IZustellung
{
    public async Task ZustelleAsync(
        SubjectId empfaenger, string art, CancellationToken cancellationToken = default)
    {
        if (steuerung.Tot.Contains(art))
        {
            throw new HttpRequestException($"{art} antwortet nicht");
        }

        // The real one for the two local kinds — they carry the order and the
        // final step, and faking them would fake the thing under test. The
        // foreign recipients are HTTP calls to services that do not run here.
        if (art is Loeschempfaenger.Schlussnachricht or Loeschempfaenger.Identitaet)
        {
            await echt.ZustelleAsync(empfaenger, art, cancellationToken);
        }

        steuerung.Zugestellt.Add(art);
    }
}

/// <summary>The cascade: order, completeness, and what a dead recipient does.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschkaskadeTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Passwort = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private MitschreibenderVersender _versand = null!;
    private readonly Probesteuerung _steuerung = new();

    public async Task InitializeAsync()
    {
        _versand = new MitschreibenderVersender(postgres.ConnectionString);

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "probe-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Singleton<IVersender>(_versand));
                dienste.AddSingleton(_steuerung);
                dienste.Replace(ServiceDescriptor.Scoped<IZustellung>(anbieter =>
                    new Probeempfaenger(
                        _steuerung,
                        ActivatorUtilities.CreateInstance<
                            WorkerTransfer.Identity.Infrastructure.Loeschung
                                .HttpLoeschzustellung>(anbieter))));
            });
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

    private async Task<(HttpClient Browser, Guid Wer, string Email)> Person()
    {
        var browser = Browser();
        var email = $"anna-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email, password = Passwort, display_name = "Anna"
        });

        var token = System.Text.RegularExpressions.Regex.Match(
            _versand.Post.Last().Text, @"token=(?<t>[A-Za-z0-9_-]+)",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5)).Groups["t"].Value;

        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email, password = Passwort });

        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "SELECT id FROM users WHERE email = @email";
        befehl.Parameters.AddWithValue("email", email);

        return (browser, (Guid)(await befehl.ExecuteScalarAsync())!, email);
    }

    private async Task<List<(string Kind, DateTime? Delivered)>> Zeilen(Guid wer)
    {
        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText =
            "SELECT kind, delivered_at FROM outbox WHERE user_id = @wer ORDER BY created_at";
        befehl.Parameters.AddWithValue("wer", wer);

        var zeilen = new List<(string, DateTime?)>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            zeilen.Add((leser.GetString(0), leser.IsDBNull(1) ? null : leser.GetDateTime(1)));
        }

        return zeilen;
    }

    private async Task<string?> Kontostand(Guid wer)
    {
        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "SELECT status::text FROM users WHERE id = @wer";
        befehl.Parameters.AddWithValue("wer", wer);

        return (string?)await befehl.ExecuteScalarAsync();
    }

    /// <summary>
    /// One pass per scope, the way the loop does it. Several passes because the
    /// order is enforced: the notice waits for the recipients, the account
    /// waits for the notice.
    /// </summary>
    private async Task Durchlauf(int male = 1)
    {
        for (var i = 0; i < male; i++)
        {
            using var bereich = _dienst.Services.CreateScope();
            await bereich.ServiceProvider
                .GetRequiredService<OutboxZusteller<IdentityDbContext>>()
                .DurchlaufAsync();
        }
    }

    [Fact]
    public async Task Das_Verlangen_sperrt_sofort_und_schreibt_zwoelf_Absichten()
    {
        var (browser, wer, _) = await Person();

        var verlangt = await browser.PostAsync("/account/erasure", null);

        verlangt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Kontostand(wer)).Should().Be("disabled", "ab jetzt passiert nichts mehr unter diesem Namen");

        var zeilen = await Zeilen(wer);
        zeilen.Should().HaveCount(12, "zehn Empfaenger, die Schlussnachricht und identity selbst");
        zeilen.Select(z => z.Kind).Should().Contain(LoeschungVerlangenHandler.Absichten);
    }

    /// <summary>
    /// Who is a recipient is decided by one rule: does the service hold a row
    /// per person. github is gone with its service (ADR-0022 deleted
    /// worker-github because it scored people); notification took its place
    /// because it holds the preferences, keyed by the person.
    /// </summary>
    [Fact]
    public void Empfaenger_ist_wer_eine_Zeile_je_Mensch_haelt()
    {
        Loeschempfaenger.Fremde.Should().BeEquivalentTo(
        [
            "consent", "profile", "resume", "portfolio",
            "applications", "transfer", "github", "notification", "scout", "advisor"
        ]);

        // `github` gehoert dazu: der Dienst haelt eine Zeile je Mensch — die
        // Verknuepfung „dieser Plattform-Mensch ist jener GitHub-Name". Er
        // stand einmal nicht auf dieser Liste, weil das geloeschte PAKET
        // `worker-github` (das Menschen bewertete) mit dem DIENST verwechselt
        // wurde, der unter ADR-0022 gebaut ist und das Gegenteil tut.
        Loeschempfaenger.Fremde.Should().Contain("github");

        // `scout` kam mit seiner ersten Tabelle dazu (ADR-0036). Er haelt
        // gespeicherte SUCHEN — die Filter, nie ein Ergebnis — und jede gehoert
        // dem Menschen, der sie abgelegt hat; dazu Ausgangszeilen, die von
        // einem Menschen handeln. Wer eine Zeile je Mensch haelt, ist
        // Empfaenger: das ist die ganze Regel.
        Loeschempfaenger.Fremde.Should().Contain("scout");

        // `advisor` kam mit seiner ersten Tabelle dazu (ADR-0037). Er haelt ein
        // MANDAT, dessen Schluessel die Person IST — Eintrittstermin,
        // Gehaltsspanne, Pensum, ausgeschlossene Unternehmen — und die
        // Gespraeche, in denen sie steht. Sichtbarkeit haelt er ausdruecklich
        // nicht; die faellt bei consent-service. Wer eine Zeile je Mensch
        // haelt, ist Empfaenger: das ist die ganze Regel.
        Loeschempfaenger.Fremde.Should().Contain("advisor");

        Loeschempfaenger.Fremde.Should().NotContain("jobs", "haelt nichts Personenbezogenes");
        Loeschempfaenger.Fremde.Should().NotContain("companies");
    }

    [Fact]
    public async Task Nach_dem_Verlangen_traegt_die_Anmeldung_nicht_mehr()
    {
        var (browser, _, email) = await Person();

        await browser.PostAsync("/account/erasure", null);

        var erneut = await Browser().PostAsJsonAsync(
            "/auth/login", new { email, password = Passwort });

        erneut.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Pressing twice is not an error, and the second press must not start a
    /// second cascade: eight more rows would be eight more deliveries for a
    /// thing that happens once.
    /// </summary>
    [Fact]
    public async Task Zweimal_verlangen_erzeugt_keine_zweite_Kaskade()
    {
        var (browser, wer, _) = await Person();

        await browser.PostAsync("/account/erasure", null);
        var zweite = await browser.PostAsync("/account/erasure", null);

        zweite.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Zeilen(wer)).Should().HaveCount(12);
    }

    /// <summary>
    /// A message saying "everything is gone" while a service still holds data
    /// is the one lie this whole cascade exists to prevent.
    /// </summary>
    [Fact]
    public async Task Ein_toter_Empfaenger_blockiert_die_Schlussnachricht_und_das_Konto()
    {
        var (browser, wer, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        _steuerung.Tot.Add(Loeschempfaenger.Art("resume"));

        await Durchlauf(3);

        var zeilen = await Zeilen(wer);

        zeilen.Single(z => z.Kind == Loeschempfaenger.Art("resume")).Delivered
            .Should().BeNull("der Empfaenger antwortet nicht");
        zeilen.Single(z => z.Kind == Loeschempfaenger.Schlussnachricht).Delivered
            .Should().BeNull("sie darf erst raus, wenn alle quittiert haben");
        zeilen.Single(z => z.Kind == Loeschempfaenger.Identitaet).Delivered
            .Should().BeNull("und das Konto faellt zuletzt");

        (await Kontostand(wer)).Should().Be("disabled", "es steht noch, aber gesperrt");
    }

    /// <summary>
    /// Orderly waiting is not a failure. Without its own answer the enforced
    /// order would look exactly like a broken recipient — and under an attempt
    /// ceiling it would strangle the delivery.
    /// </summary>
    [Fact]
    public async Task Geordnetes_Warten_verbraucht_keinen_Versuch()
    {
        var (browser, wer, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        _steuerung.Tot.Add(Loeschempfaenger.Art("consent"));
        await Durchlauf(3);

        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText =
            "SELECT attempts, last_error FROM outbox WHERE user_id = @wer AND kind = @art";
        befehl.Parameters.AddWithValue("wer", wer);
        befehl.Parameters.AddWithValue("art", Loeschempfaenger.Schlussnachricht);

        await using var leser = await befehl.ExecuteReaderAsync();
        (await leser.ReadAsync()).Should().BeTrue();
        leser.GetInt32(0).Should().Be(0, "warten ist kein Fehlschlag");
        leser.GetString(1).Should().BeEmpty();
    }

    [Fact]
    public async Task Wenn_alle_quittiert_haben_faellt_das_Konto_und_die_Mail_geht_raus()
    {
        var (browser, wer, email) = await Person();
        await browser.PostAsync("/account/erasure", null);

        await Durchlauf(3);

        var zeilen = await Zeilen(wer);
        zeilen.Should().OnlyContain(z => z.Delivered != null, "alle zwoelf sind durch");

        (await Kontostand(wer)).Should().BeNull("die Zeile ist weg");

        var schluss = _versand.Post.Last(p => p.An == email);
        schluss.Betreff.Should().Contain("gelöscht");
        // Says only THAT it is done. Listing what was deleted would copy the
        // data into an inbox that may not be the person's alone.
        schluss.Text.Should().NotContain("profile").And.NotContain("resume");
    }

    /// <summary>
    /// The proof is the set of rows, and it is a query rather than a guess.
    /// </summary>
    [Fact]
    public async Task Offen_heisst_offen_und_leer_heisst_fertig()
    {
        var (browser, wer, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        using var bereich = _dienst.Services.CreateScope();
        var bestand = bereich.ServiceProvider.GetRequiredService<ILoeschbestand>();

        (await bestand.OffeneAbsichtenAsync(new SubjectId(wer))).Should().HaveCount(12);

        await Durchlauf(3);

        (await bestand.OffeneAbsichtenAsync(new SubjectId(wer))).Should().BeEmpty();
    }

    /// <summary>
    /// A company left without an administrator is put dormant, not deleted: it
    /// is not a natural person, and its adverts are still its own. The intent
    /// deliberately carries no erasure prefix, so a silent jobs-service cannot
    /// hold a person's erasure open.
    /// </summary>
    [Fact]
    public async Task Die_letzte_Administratorin_legt_ihr_Unternehmen_still()
    {
        var browser = Browser();
        var domain = $"firma-{Guid.NewGuid():N}.example";
        var email = $"chefin-{Guid.NewGuid():N}@{domain}";

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email, password = Passwort, display_name = "Chefin", company_name = "Beispiel GmbH"
        });
        var token = System.Text.RegularExpressions.Regex.Match(
            _versand.Post.Last().Text, @"token=(?<t>[A-Za-z0-9_-]+)",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5)).Groups["t"].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email, password = Passwort });

        await browser.PostAsync("/account/erasure", null);
        await Durchlauf(3);

        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "SELECT status FROM tenants WHERE domain = @domain";
        befehl.Parameters.AddWithValue("domain", domain);

        (await befehl.ExecuteScalarAsync()).Should().Be("dormant");
    }
}
