using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Tests;

/// <summary>Die Löschung — hier ohne jede Ausnahme.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschungTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";
    private const string Meldegeheimnis = "melde-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:notification", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Identity:Adresse", "http://identity.test");
            host.UseSetting("Identity:Geheimnis", Meldegeheimnis);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IPostbote, Probepostbote>()));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inbox und Einstellungen fallen — ohne Schalter, ohne Ausnahme.
    /// </summary>
    /// <remarks>
    /// Es gibt hier keine Zeile, in der jemand nur <em>gehandelt</em> hätte;
    /// jede handelt <em>von</em> der Person.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_Postfach_und_Einstellungen_mit()
    {
        var anna = Guid.CreateVersion7();
        await Melde(anna);
        await Wuensche(anna);

        await using (var vorher = postgres.Kontext())
        {
            (await vorher.Eingaenge.AnyAsync(row => row.UserId == anna)).Should().BeTrue();
            (await vorher.Wuensche.AnyAsync(row => row.Id == anna)).Should().BeTrue();
        }

        var response = await Loesche(anna);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var nachher = postgres.Kontext();
        (await nachher.Eingaenge.AnyAsync(row => row.UserId == anna)).Should().BeFalse();
        (await nachher.Wuensche.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>Die Löschung eines Menschen fasst niemand anderen an.</summary>
    [Fact]
    public async Task Die_Loeschung_trifft_nur_diesen_Menschen()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();
        await Melde(anna);
        await Melde(bea);

        await Loesche(anna);

        await using var context = postgres.Kontext();
        (await context.Eingaenge.AnyAsync(row => row.UserId == bea)).Should().BeTrue();
    }

    /// <summary>Ohne das Geheimnis passiert nichts.</summary>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_geloescht()
    {
        var anna = Guid.CreateVersion7();
        await Melde(anna);

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Kontext();
        (await context.Eingaenge.AnyAsync(row => row.UserId == anna)).Should().BeTrue();
    }

    /// <summary>
    /// Das Löschgeheimnis ist ein anderes als das der Meldung.
    /// </summary>
    /// <remarks>
    /// „Darf eine Mail anstoßen" und „darf alles über einen Menschen löschen"
    /// dürfen nicht dasselbe Papier sein (ADR-0027 §4.4).
    /// </remarks>
    [Fact]
    public async Task Das_Meldegeheimnis_oeffnet_die_Loeschung_nicht()
    {
        var anna = Guid.CreateVersion7();
        await Melde(anna);

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Meldegeheimnis);

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Kontext();
        (await context.Eingaenge.AnyAsync(row => row.UserId == anna)).Should().BeTrue();
    }

    /// <summary>Ein zweiter Aufruf ist kein Fehlschlag.</summary>
    [Fact]
    public async Task Ein_zweites_Mal_loeschen_ist_wieder_2xx()
    {
        var anna = Guid.CreateVersion7();

        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> Loesche(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
        return browser.PostAsJsonAsync("/erasure", new { user_id = subject });
    }

    /// <summary>Legt einen Eingang an — und besteht darauf, dass es klappt.</summary>
    /// <remarks>
    /// Die Antwort wird GEPRUEFT. Vorher wurde sie weggeworfen, und ein
    /// fehlgeschlagenes Befuellen meldete sich erst drei Zeilen spaeter als
    /// „kein Eintrag" — eine Ursache, die wie eine Wirkung aussieht.
    /// </remarks>
    private async Task<HttpResponseMessage> Melde(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        var response = await browser.PostAsJsonAsync(
            "/internal/notifications", new { user_id = subject, kind = "market_request" });

        response.IsSuccessStatusCode.Should().BeTrue(
            "das Befuellen muss gelingen, sonst prueft der Test etwas anderes — "
            + $"bekam {(int)response.StatusCode}: "
            + await response.Content.ReadAsStringAsync());

        return response;
    }

    private Task<HttpResponseMessage> Wuensche(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(subject));

        return browser.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = false,
            market_request = true,
            application_update = true,
            transfer_update = true
        });
    }
}
