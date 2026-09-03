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

/// <summary>Die Einstellungen — und das Geheimnis, das nie zurückkommt.</summary>
/// <remarks>
/// Die wichtigste Zusage dieser Reihe steht in
/// <c>Der_Schluessel_kommt_nie_zurueck</c>: die Oberfläche erfährt, DASS einer
/// hinterlegt ist, und seine letzten vier Zeichen. Alles andere wäre ein
/// Geheimnis, das man abrufen — und damit abziehen — kann.
/// <para>
/// Und eine, die man leicht übersieht: nach einer Löschung darf keine Zeile mit
/// einem verschlüsselten Schlüssel stehen bleiben. `account_settings` hat
/// `subject_id` als PRIMÄRSCHLÜSSEL und zeigt auf nichts, also räumt kein
/// CASCADE sie mit weg.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class EinstellungenTests(Postgres postgres) : IAsyncLifetime
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
            // Der Hauptschluessel. Ohne ihn wirft der Dienst beim Aufloesen —
            // und genau das soll er, siehe `Geheimnisspeicher`.
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

    private async Task<(HttpClient Browser, Guid Wer)> Angemeldet()
    {
        var adresse = $"anna-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = adresse, password = Passwort, display_name = "Anna"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = adresse, password = Passwort });

        var sitzung = JsonDocument.Parse(
            await (await browser.GetAsync("/auth/session")).Content.ReadAsStringAsync());

        return (browser, sitzung.RootElement.GetProperty("user").GetProperty("user_id").GetGuid());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Wer nie etwas gesetzt hat, bekommt die zurückhaltendste Stellung.</summary>
    /// <remarks>
    /// Kein Anbieter, kein Verfall, kein Protokoll. Eine Voreinstellung auf
    /// einen Fremdanbieter wäre eine Einwilligung, die niemand gegeben hat.
    /// </remarks>
    [Fact]
    public async Task Ohne_Zeile_gilt_die_zurueckhaltendste_Stellung()
    {
        var (browser, _) = await Angemeldet();

        var stand = await Json(await browser.GetAsync("/account/settings"));

        stand.GetProperty("ai_provider").GetString().Should().Be("none");
        stand.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();
        stand.GetProperty("ai_audit_log").GetBoolean().Should().BeFalse();
        stand.GetProperty("delete_after_months").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>DER wichtigste Test: das Geheimnis geht nur in eine Richtung.</summary>
    [Fact]
    public async Task Der_Schluessel_kommt_nie_zurueck()
    {
        var (browser, _) = await Angemeldet();
        const string schluessel = "sk-ant-api03-GEHEIM-abcd1234WXYZ";

        (await browser.PutAsJsonAsync("/account/ai-key", new { key = schluessel }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var antwort = await browser.GetAsync("/account/settings");
        var roh = await antwort.Content.ReadAsStringAsync();
        var stand = JsonDocument.Parse(roh).RootElement;

        stand.GetProperty("ai_key_present").GetBoolean().Should().BeTrue();
        stand.GetProperty("ai_key_tail").GetString().Should().Be("WXYZ");

        // Der ganze Rumpf, nicht nur das eine Feld: der Schlüssel darf nirgends
        // darin auftauchen, auch nicht als Teil eines anderen.
        roh.Should().NotContain(schluessel);
        roh.Should().NotContain("GEHEIM");
    }

    /// <summary>In der Datenbank liegt er verschlüsselt, nicht im Klartext.</summary>
    [Fact]
    public async Task In_der_Zeile_liegt_er_verschluesselt()
    {
        var (browser, wer) = await Angemeldet();
        const string schluessel = "sk-test-KLARTEXT-0000";

        await browser.PutAsJsonAsync("/account/ai-key", new { key = schluessel });

        using var bereich = _dienst.Services.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var zeile = await kontext.AccountSettings.FirstAsync(e => e.SubjectId == wer);

        zeile.AiKeyEncrypted.Should().NotBeEmpty();
        zeile.AiKeyEncrypted.Should().NotContain("KLARTEXT");
        zeile.AiKeyTail.Should().Be("0000");

        // Und der Rundlauf stimmt: was gespeichert wurde, kommt wieder heraus.
        var speicher = bereich.ServiceProvider.GetRequiredService<Geheimnisspeicher>();
        speicher.Entschluessele(zeile.AiKeyEncrypted).Should().Be(schluessel);
    }

    /// <summary>Ein leerer Schlüssel ENTFERNT — er lässt nicht unverändert.</summary>
    /// <remarks>
    /// Ein Feld, das bei leer nichts tut, hat keinen Weg zurück zu „keiner" —
    /// und dann bliebe ein Geheimnis liegen, das niemand mehr braucht.
    /// </remarks>
    [Fact]
    public async Task Ein_leerer_Schluessel_entfernt_ihn()
    {
        var (browser, _) = await Angemeldet();

        await browser.PutAsJsonAsync("/account/ai-key", new { key = "sk-test-1234" });
        await browser.PutAsJsonAsync("/account/ai-key", new { key = "" });

        var stand = await Json(await browser.GetAsync("/account/settings"));
        stand.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();
        stand.GetProperty("ai_key_tail").GetString().Should().BeEmpty();
    }

    /// <summary>„Kein Anbieter" räumt den Schlüssel mit weg.</summary>
    /// <remarks>
    /// Ein Geheimnis ohne Zweck ist das schlechteste, was man aufbewahren kann.
    /// </remarks>
    [Fact]
    public async Task Kein_Anbieter_raeumt_den_Schluessel_weg()
    {
        var (browser, _) = await Angemeldet();

        await browser.PutAsJsonAsync("/account/ai-key", new { key = "sk-test-1234" });
        await browser.PutAsJsonAsync("/account/settings", new
        {
            delete_after_months = (int?)null,
            ai_provider = "none",
            ai_base_url = "",
            ai_model = "",
            ai_audit_log = false
        });

        var stand = await Json(await browser.GetAsync("/account/settings"));
        stand.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();
    }

    /// <summary>Ein zu kurzer Verfall wird abgelehnt, nicht stillschweigend gerundet.</summary>
    /// <remarks>
    /// Ein Konto, das nach vier Wochen verschwindet, verliert jemand im Urlaub.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    public async Task Ein_unsinniger_Verfall_wird_abgelehnt(int monate)
    {
        var (browser, _) = await Angemeldet();

        var antwort = await browser.PutAsJsonAsync("/account/settings", new
        {
            delete_after_months = monate,
            ai_provider = "none",
            ai_base_url = "",
            ai_model = "",
            ai_audit_log = false
        });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ohne Anmeldung geht nichts — weder lesen noch schreiben.</summary>
    [Fact]
    public async Task Ohne_Anmeldung_geht_nichts()
    {
        (await Browser().GetAsync("/account/settings"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Browser().PutAsJsonAsync("/account/ai-key", new { key = "sk-fremd" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Die Löschung nimmt die Einstellungen mit — samt Geheimnis.</summary>
    /// <remarks>
    /// `account_settings` hat `subject_id` als PRIMÄRSCHLÜSSEL und zeigt auf
    /// nichts. Kein CASCADE räumt sie weg; ohne die eigene Zeile im
    /// Löschbestand bliebe ein verschlüsselter Schlüssel für ein Konto stehen,
    /// das es nicht mehr gibt.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_die_Einstellungen_mit()
    {
        var (browser, wer) = await Angemeldet();
        await browser.PutAsJsonAsync("/account/ai-key", new { key = "sk-test-bleibt-nicht" });

        using (var vorher = _dienst.Services.CreateScope())
        {
            var kontext = vorher.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await kontext.AccountSettings.AnyAsync(e => e.SubjectId == wer))
                .Should().BeTrue("ohne Zeile prüft dieser Test nichts");
        }

        (await browser.PostAsync("/account/erasure", null))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Die Kaskade läuft asynchron; hier zählt der Schritt, der die Zeilen
        // dieses Dienstes räumt. Er wird über den Bestand direkt gefahren.
        using var bereich = _dienst.Services.CreateScope();
        var bestand = bereich.ServiceProvider
            .GetRequiredService<Application.Loeschung.ILoeschbestand>();
        await bestand.SchliesseAbAsync(new Girder.Core.Identity.SubjectId(wer), DateTimeOffset.UtcNow);

        var danach = bereich.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await danach.AccountSettings.AnyAsync(e => e.SubjectId == wer)).Should().BeFalse();
    }
}
