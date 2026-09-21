using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

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
public sealed class Probeempfaenger(Probesteuerung steuerung, IDelivery echt) : IDelivery
{
    public async Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        if (steuerung.Tot.Contains(kind))
        {
            throw new HttpRequestException($"{kind} antwortet nicht");
        }

        // The real one for the two local kinds — they carry the order and the
        // final step, and faking them would fake the thing under test. The
        // foreign recipients are HTTP calls to services that do not run here.
        if (kind is ErasureRecipient.Schlussnachricht or ErasureRecipient.Identitaet)
        {
            await echt.DeliverAsync(recipient, kind, cancellationToken);
        }

        steuerung.Zugestellt.Add(kind);
    }
}

/// <summary>The cascade: order, completeness, and what a dead recipient does.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschkaskadeTests(Postgres postgres) : IAsyncLifetime
{
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
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:PrivateKey", Tokenform.PrivatSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "probe-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IVersender>(_versand));
                services.AddSingleton(_steuerung);
                services.Replace(ServiceDescriptor.Scoped<IDelivery>(provider =>
                    new Probeempfaenger(
                        _steuerung,
                        ActivatorUtilities.CreateInstance<
                            WorkerTransfer.Identity.Infrastructure.Erasure
                                .HttpErasureDelivery>(provider))));
            });
        });

        using var scope = _dienst.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
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

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = "SELECT id FROM users WHERE email = @email";
        befehl.Parameters.AddWithValue("email", email);

        return (browser, (Guid)(await befehl.ExecuteScalarAsync())!, email);
    }

    private async Task<List<(string Kind, DateTime? Delivered)>> Zeilen(Guid subject)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText =
            "SELECT kind, delivered_at FROM outbox WHERE user_id = @wer ORDER BY created_at";
        befehl.Parameters.AddWithValue("wer", subject);

        var rows = new List<(string, DateTime?)>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            rows.Add((leser.GetString(0), leser.IsDBNull(1) ? null : leser.GetDateTime(1)));
        }

        return rows;
    }

    private async Task<string?> Kontostand(Guid subject)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = "SELECT status::text FROM users WHERE id = @wer";
        befehl.Parameters.AddWithValue("wer", subject);

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
            using var scope = _dienst.Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<OutboxDispatcher<IdentityDbContext>>()
                .PassAsync();
        }
    }

    [Fact]
    public async Task Das_Verlangen_sperrt_sofort_und_schreibt_dreizehn_Absichten()
    {
        var (browser, subject, _) = await Person();

        var verlangt = await browser.PostAsync("/account/erasure", null);

        verlangt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Kontostand(subject)).Should().Be("disabled", "ab jetzt passiert nichts mehr unter diesem Namen");

        var rows = await Zeilen(subject);
        rows.Should().HaveCount(13, "elf Empfaenger, die Schlussnachricht und identity selbst");
        rows.Select(z => z.Kind).Should().Contain(RequestErasureHandler.Absichten);
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
        ErasureRecipient.Fremde.Should().BeEquivalentTo(
        [
            "consent", "profile", "resume", "portfolio",
            "applications", "transfer", "github", "notification", "scout", "advisor",
            "assessment"
        ]);

        // `github` gehoert dazu: der Dienst haelt eine Zeile je Mensch — die
        // Verknuepfung „dieser Plattform-Mensch ist jener GitHub-Name". Er
        // stand einmal nicht auf dieser Liste, weil das geloeschte PAKET
        // `worker-github` (das Menschen bewertete) mit dem DIENST verwechselt
        // wurde, der unter ADR-0022 gebaut ist und das Gegenteil tut.
        ErasureRecipient.Fremde.Should().Contain("github");

        // `scout` kam mit seiner ersten Tabelle dazu (ADR-0036). Er haelt
        // gespeicherte SUCHEN — die Filter, nie ein Ergebnis — und jede gehoert
        // dem Menschen, der sie abgelegt hat; dazu Ausgangszeilen, die von
        // einem Menschen handeln. Wer eine Zeile je Mensch haelt, ist
        // Empfaenger: das ist die ganze Regel.
        ErasureRecipient.Fremde.Should().Contain("scout");

        // `advisor` kam mit seiner ersten Tabelle dazu (ADR-0037). Er haelt ein
        // MANDAT, dessen Schluessel die Person IST — Eintrittstermin,
        // Gehaltsspanne, Pensum, ausgeschlossene Unternehmen — und die
        // Gespraeche, in denen sie steht. Sichtbarkeit haelt er ausdruecklich
        // nicht; die faellt bei consent-service. Wer eine Zeile je Mensch
        // haelt, ist Empfaenger: das ist die ganze Regel.
        ErasureRecipient.Fremde.Should().Contain("advisor");

        // `assessment` kam mit seiner ersten Tabelle dazu (ADR-0042). Er haelt
        // die Aufgaben, die einem Menschen gestellt wurden, was er abgegeben
        // hat und die eine Rueckmeldung darauf — alles unter seiner Kennung.
        // UND KEINEN AUFBEWAHRUNGSFALL, auch nicht fuer die Evaluation: sie ist
        // kein Evidence, der jemand anderem gehoert, und eine, die eine Erasure
        // ueberlebte, waere genau das Reference, das ADR-0042 ausschliesst.
        ErasureRecipient.Fremde.Should().Contain("assessment");

        ErasureRecipient.Fremde.Should().NotContain("jobs", "haelt nichts Personenbezogenes");
        ErasureRecipient.Fremde.Should().NotContain("companies");
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
        var (browser, subject, _) = await Person();

        await browser.PostAsync("/account/erasure", null);
        var zweite = await browser.PostAsync("/account/erasure", null);

        zweite.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Zeilen(subject)).Should().HaveCount(13);
    }

    /// <summary>
    /// A message saying "everything is gone" while a service still holds data
    /// is the one lie this whole cascade exists to prevent.
    /// </summary>
    [Fact]
    public async Task Ein_toter_Empfaenger_blockiert_die_Schlussnachricht_und_das_Konto()
    {
        var (browser, subject, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        _steuerung.Tot.Add(ErasureRecipient.Art("resume"));

        await Durchlauf(3);

        var rows = await Zeilen(subject);

        rows.Single(z => z.Kind == ErasureRecipient.Art("resume")).Delivered
            .Should().BeNull("der Empfaenger antwortet nicht");
        rows.Single(z => z.Kind == ErasureRecipient.Schlussnachricht).Delivered
            .Should().BeNull("sie darf erst raus, wenn alle quittiert haben");
        rows.Single(z => z.Kind == ErasureRecipient.Identitaet).Delivered
            .Should().BeNull("und das Konto faellt zuletzt");

        (await Kontostand(subject)).Should().Be("disabled", "es steht noch, aber gesperrt");
    }

    /// <summary>
    /// Orderly waiting is not a failure. Without its own answer the enforced
    /// order would look exactly like a broken recipient — and under an attempt
    /// ceiling it would strangle the delivery.
    /// </summary>
    [Fact]
    public async Task Geordnetes_Warten_verbraucht_keinen_Versuch()
    {
        var (browser, subject, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        _steuerung.Tot.Add(ErasureRecipient.Art("consent"));
        await Durchlauf(3);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText =
            "SELECT attempts, last_error FROM outbox WHERE user_id = @wer AND kind = @art";
        befehl.Parameters.AddWithValue("wer", subject);
        befehl.Parameters.AddWithValue("art", ErasureRecipient.Schlussnachricht);

        await using var leser = await befehl.ExecuteReaderAsync();
        (await leser.ReadAsync()).Should().BeTrue();
        leser.GetInt32(0).Should().Be(0, "warten ist kein Fehlschlag");
        leser.GetString(1).Should().BeEmpty();
    }

    [Fact]
    public async Task Wenn_alle_quittiert_haben_faellt_das_Konto_und_die_Mail_geht_raus()
    {
        var (browser, subject, email) = await Person();
        await browser.PostAsync("/account/erasure", null);

        await Durchlauf(3);

        var rows = await Zeilen(subject);
        rows.Should().OnlyContain(z => z.Delivered != null, "alle dreizehn sind durch");

        (await Kontostand(subject)).Should().BeNull("die Zeile ist weg");

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
        var (browser, subject, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        using var scope = _dienst.Services.CreateScope();
        var bestand = scope.ServiceProvider.GetRequiredService<IErasureLedger>();

        (await bestand.OpenIntentsAsync(new SubjectId(subject))).Should().HaveCount(13);

        await Durchlauf(3);

        (await bestand.OpenIntentsAsync(new SubjectId(subject))).Should().BeEmpty();
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

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = "SELECT status FROM tenants WHERE domain = @domain";
        befehl.Parameters.AddWithValue("domain", domain);

        (await befehl.ExecuteScalarAsync()).Should().Be("dormant");
    }
}
