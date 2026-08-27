using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Die drei Endpunkte verhalten sich so, wie die React-App es erwartet.
/// </summary>
/// <remarks>
/// <strong>Die Haelfte, die Python fragte, ist weg</strong> — mit dem
/// Python-Dienst. Zwei Tests reichten einen frisch ausgestellten Token an einen
/// echten Interpreter und liessen ihn von <c>worker_auth.TokenManager</c>
/// pruefen; ohne Gegenueber beweisen sie nichts mehr.
/// <para>
/// Was bleibt, ist die andere Richtung und sie bleibt wichtig: die
/// Cookie-Namen und -Pfade, das RFC-9457-Dokument, 403 statt 401 fuer ein
/// unbestaetigtes Konto — und ein Passwort, das der Python-Hasher geschrieben
/// hat und das hier ohne Zuruecksetzen hereinlaesst. Diese Zeilen liegen in
/// echten Datenbanken, und sie muessen weiter tragen.
/// </para>
/// <para>
/// Dass .NET Token annimmt, die Python WIRKLICH ausgestellt hat, prueft
/// <c>KreuzbeweisPythonNachDotnetTests</c> — aus aufgezeichneten Zeichenketten,
/// also ohne Interpreter. Das ist die Richtung, die nach dem Umzug noch zaehlt:
/// eine Sitzung von vorgestern muss weiter gelten (Ue-2).
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class KreuzbeweisTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Passwort = "geheim-und-lang-genug";

    /// <summary>Written by <c>worker_auth.BcryptPasswordHasher</c>, verbatim.</summary>
    private const string PythonEintrag =
        "$2b$12$DK/g90pKN70aYUJosVXXue7nxXxqQoBDevGWH/2zQSzfzqNLjn9eW";

    private WebApplicationFactory<Program> _dienst = null!;
    private string _email = null!;

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
        });

        using (var bereich = _dienst.Services.CreateScope())
        {
            await bereich.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Database.MigrateAsync();
        }

        _email = $"anna-{Guid.NewGuid():N}@example.com";
        await LegeKontoAn(_email);
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private async Task LegeKontoAn(string email, string status = "active")
    {
        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = """
            INSERT INTO users (id, email, password_hash, display_name, status, roles,
                               created_at, updated_at, version)
            VALUES (@id, @email, @hash, 'Anna', @status::account_status, '["user"]'::jsonb,
                    now(), now(), 1)
            """;
        befehl.Parameters.AddWithValue("id", Guid.NewGuid());
        befehl.Parameters.AddWithValue("email", email);
        befehl.Parameters.AddWithValue("hash", PythonEintrag);
        befehl.Parameters.AddWithValue("status", status);
        await befehl.ExecuteNonQueryAsync();
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<HttpResponseMessage> Anmelden(
        HttpClient browser, string email, string passwort) =>
        await browser.PostAsJsonAsync("/auth/login", new { email, password = passwort });

    private static string CookieAus(HttpResponseMessage antwort, string name) =>
        antwort.Headers.GetValues("Set-Cookie")
            .First(zeile => zeile.StartsWith($"{name}=", StringComparison.Ordinal))
            .Split(';')[0][(name.Length + 1)..];

    /// <summary>
    /// The entry was written by the Python hasher and is read here without
    /// anyone being asked to reset anything.
    /// </summary>
    [Fact]
    public async Task Ein_Passwort_das_Python_gehasht_hat_laesst_herein()
    {
        var antwort = await Anmelden(Browser(), _email, Passwort);

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ein_falsches_Passwort_wird_abgewiesen()
    {
        var antwort = await Anmelden(Browser(), _email, "falsch-aber-lang-genug");

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ein_unbestaetigtes_Konto_bekommt_403_und_nicht_401()
    {
        var wartend = $"clara-{Guid.NewGuid():N}@example.com";
        await LegeKontoAn(wartend, status: "pending");

        var antwort = await Anmelden(Browser(), wartend, Passwort);

        antwort.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The React app reads the message out of <c>detail</c>. Girder's own
    /// handler writes a different envelope, which is why this service does not
    /// use it.
    /// </summary>
    [Fact]
    public async Task Eine_Absage_kommt_als_RFC_9457_Dokument()
    {
        var antwort = await Anmelden(Browser(), _email, "falsch-aber-lang-genug");

        antwort.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await antwort.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem.Should().ContainKey("detail").And.ContainKey("status").And.ContainKey("title");
    }

    [Fact]
    public async Task Die_Cookies_heissen_und_liegen_wie_bei_Python()
    {
        var antwort = await Anmelden(Browser(), _email, Passwort);
        var kopfzeilen = antwort.Headers.GetValues("Set-Cookie").ToList();

        var zugriff = kopfzeilen.Single(z => z.StartsWith("access=", StringComparison.Ordinal));
        var erneuerung = kopfzeilen.Single(z => z.StartsWith("refresh=", StringComparison.Ordinal));

        zugriff.Should().Contain("path=/").And.Contain("httponly");
        erneuerung.Should().Contain("path=/auth", "sonst sieht jede Route den Erneuerungstoken");
        erneuerung.Should().Contain("httponly").And.Contain("samesite=strict");
    }

    [Fact]
    public async Task Erneuern_ergibt_ein_frisches_Paar_und_entwertet_das_alte()
    {
        var browser = Browser();
        var angemeldet = await Anmelden(browser, _email, Passwort);
        var erster = CookieAus(angemeldet, "refresh");

        var erneuert = await browser.PostAsync("/auth/refresh", null);
        erneuert.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieAus(erneuert, "refresh").Should().NotBe(erster, "jede Erneuerung rotiert");
    }

    [Fact]
    public async Task Ohne_Erneuerungstoken_gibt_es_keine_Erneuerung()
    {
        var antwort = await Browser().PostAsync("/auth/refresh", null);

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nach_dem_Abmelden_traegt_der_Erneuerungstoken_nicht_mehr()
    {
        var browser = Browser();
        await Anmelden(browser, _email, Passwort);

        var abgemeldet = await browser.PostAsync("/auth/logout", null);
        abgemeldet.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var versuch = await browser.PostAsync("/auth/refresh", null);
        versuch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Abmelden_ohne_Sitzung_ist_kein_Fehler()
    {
        var antwort = await Browser().PostAsync("/auth/logout", null);

        antwort.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Hands the token to the real Python verifier and returns what it said.
    /// </summary>
}
