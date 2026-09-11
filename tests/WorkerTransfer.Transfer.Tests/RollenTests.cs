using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults.Rollen;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>
/// Die Linie liegt am Geld, nicht am Gespräch.
/// </summary>
/// <remarks>
/// <para>Interesse zeigen, jemanden ansprechen, die eigenen Vorgänge lesen —
/// Anbahnung, und damit die Arbeit, für die jemand eingeladen wird. Ein
/// <em>Angebot</em> nennt Eintrittstermin und Vermittlungsgebühr, und ein
/// <em>Abschluss</em> stellt fest, dass beides gilt: beides bindet das
/// Unternehmen.</para>
///
/// <para><strong><c>withdraw</c> steht bewusst nicht dabei</strong>, und die
/// Reihe dazu steht hier: wer einen Vorgang anfangen darf, muss ihn beenden
/// dürfen. Sonst ist die Einladung eine Falle.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RollenTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeledger _ledger = new();
    private readonly Rollenprobe _rollen = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _ledger));
                dienste.Replace(ServiceDescriptor.Scoped<IZustellung, Probezustellung>());
                dienste.Replace(ServiceDescriptor.Scoped<IFirmenrollen>(_ => _rollen));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private HttpClient AlsFirma(Guid firma, Guid? wer = null) =>
        Mit(Tokenform.Firma(wer ?? Guid.CreateVersion7(), firma));

    private HttpClient AlsPerson(Guid wer) => Mit(Tokenform.Person(wer));

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Ein laufender Vorgang, bis kurz vor das Angebot.</summary>
    private async Task<(Guid Vorgang, Guid Wer)> ImGespraech(Guid firma)
    {
        var wer = Guid.CreateVersion7();

        await AlsPerson(wer).PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        _ledger.MarktFrei.Add((wer, firma));

        var angelegt = await AlsFirma(firma).PostAsJsonAsync("/transfers", new
        {
            subject_id = wer, message = "Wir hätten da etwas."
        });

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        (await AlsPerson(wer).PostAsync($"/transfers/{id}/accept-talk", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (id, wer);
    }

    private static Task<HttpResponseMessage> Angebot(HttpClient browser, Guid vorgang) =>
        browser.PostAsJsonAsync($"/transfers/{vorgang}/offer", new
        {
            note = "Wir bieten die Stelle an.", start_on = "2026-10", fee_cents = 250_000L
        });

    /// <summary>Ein <c>member</c> macht kein Angebot.</summary>
    [Fact]
    public async Task Nur_ein_Administrator_macht_ein_Angebot()
    {
        var firma = Guid.CreateVersion7();
        var (vorgang, _) = await ImGespraech(firma);

        _rollen.Antwort = Firmenrolle.Mitglied;
        var alsMitglied = await Angebot(AlsFirma(firma), vorgang);

        alsMitglied.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, "ein Angebot nennt Termin und Gebuehr");

        _rollen.Antwort = Firmenrolle.Admin;
        var alsAdmin = await Angebot(AlsFirma(firma), vorgang);

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Und schliesst keinen Vorgang ab.</summary>
    [Fact]
    public async Task Nur_ein_Administrator_schliesst_einen_Vorgang_ab()
    {
        var firma = Guid.CreateVersion7();
        var (vorgang, wer) = await ImGespraech(firma);

        _rollen.Antwort = Firmenrolle.Admin;
        (await Angebot(AlsFirma(firma), vorgang)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Kein `confirm-release`: die Person hier ist nicht beschaeftigt, es
        // gibt also niemanden, der sie gehen lassen muesste.
        (await AlsPerson(wer).PostAsync($"/transfers/{vorgang}/accept-offer", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _rollen.Antwort = Firmenrolle.Mitglied;
        var alsMitglied = await AlsFirma(firma).PostAsync($"/transfers/{vorgang}/complete", null);

        alsMitglied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _rollen.Antwort = Firmenrolle.Admin;
        var alsAdmin = await AlsFirma(firma).PostAsync($"/transfers/{vorgang}/complete", null);

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(alsAdmin)).GetProperty("status").GetString().Should().Be("completed");
    }

    /// <summary>
    /// Ein <c>member</c> spricht an, zeigt Interesse — und zieht wieder
    /// zurueck.
    /// </summary>
    /// <remarks>
    /// Die Haelfte, die leicht verlorengeht. <c>withdraw</c> traegt absichtlich
    /// kein Firmenrecht: ein Verfahren, aus dem man nicht aussteigen kann, ist
    /// kein Verfahren, sondern eine Falle — derselbe Satz, den <c>/decline</c>
    /// fuer die Person schon traegt.
    /// </remarks>
    [Fact]
    public async Task Ein_Mitglied_zeigt_Interesse_und_zieht_es_zurueck()
    {
        var firma = Guid.CreateVersion7();
        var (vorgang, _) = await ImGespraech(firma);

        _rollen.Antwort = Firmenrolle.Mitglied;

        var zurueck = await AlsFirma(firma).PostAsync($"/transfers/{vorgang}/withdraw", null);

        zurueck.StatusCode.Should().Be(
            HttpStatusCode.OK, "wer anfangen darf, muss aufhoeren duerfen");
    }

    /// <summary>Gefragt wird nach der Firma aus dem TOKEN.</summary>
    [Fact]
    public async Task Gefragt_wird_nach_dem_Mandanten_aus_dem_Token()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var (vorgang, _) = await ImGespraech(firma);

        await Angebot(AlsFirma(firma, wer), vorgang);

        _rollen.Zuletzt.Should().Be((wer, firma));
    }

    /// <summary>Schweigt die Rollenauskunft, ist das kein Nein.</summary>
    [Fact]
    public async Task Eine_schweigende_Rollenauskunft_ist_kein_Nein()
    {
        var firma = Guid.CreateVersion7();
        var (vorgang, _) = await ImGespraech(firma);

        _rollen.Schweigt = true;
        var antwort = await Angebot(AlsFirma(firma), vorgang);

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Ohne Token 401, als Person ohne Firma 403 — die Richtlinie aendert
    /// daran nichts.
    /// </summary>
    [Fact]
    public async Task Ohne_Token_und_ohne_Firma_bleibt_es_wie_es_war()
    {
        var firma = Guid.CreateVersion7();
        var (vorgang, wer) = await ImGespraech(firma);

        (await Angebot(_dienst.CreateClient(), vorgang))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Angebot(AlsPerson(wer), vorgang))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
