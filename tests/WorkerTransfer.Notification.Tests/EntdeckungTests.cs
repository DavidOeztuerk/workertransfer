using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Tests;

/// <summary>
/// „Dein Profil wurde entdeckt" — höchstens eine je Person und Tag.
/// </summary>
/// <remarks>
/// <para>Die Auskunft aus ADR-0033, und die einzige Art mit einer
/// <em>eigenen</em> Kappe. Bei jeder anderen entsteht der Postfacheintrag immer
/// und nur die Mail wird gedrosselt: das Postfach liegt hinter der Anmeldung
/// und verrät niemandem etwas.</para>
///
/// <para>Hier ist das anders, und darin liegt der ganze Grund für diese Reihe:
/// ein Eintrag je Treffer wäre ein <strong>Zähler über die eigene
/// Sichtbarkeit</strong> — auf Umwegen genau das Verzeichnis „wer hat wen
/// angesehen", das nicht entstehen soll. Die Kappe nimmt deshalb den Eintrag
/// mit, nicht nur die Mail.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class EntdeckungTests(Postgres postgres) : IAsyncLifetime
{
    private const string Meldegeheimnis = "melde-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probepostbote _postbote = new();
    private readonly Probeuhr _uhr = new(new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:notification", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Identity:Adresse", "http://identity.test");
            host.UseSetting("Identity:Geheimnis", Meldegeheimnis);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IPostbote>(_ => _postbote));
                dienste.Replace(ServiceDescriptor.Singleton<TimeProvider>(_uhr));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Die Art gibt es, und sie kommt an.</summary>
    [Fact]
    public async Task Die_Art_gibt_es()
    {
        var anna = Guid.CreateVersion7();

        (await Melde(anna)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var postfach = await Postfach(anna);

        postfach.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("kind").GetString().Should().Be("profile_discovered");
    }

    /// <summary>Zehn Treffer an einem Tag ergeben EINEN Eintrag.</summary>
    /// <remarks>
    /// Nicht zehn und auch nicht „einen Eintrag, aber nur eine Mail": zehn
    /// Einträge wären zehn Zeilen darüber, wie oft diese Person gesehen wurde.
    /// Genau das ist der Zähler, den ADR-0033 nicht entstehen lässt.
    /// </remarks>
    [Fact]
    public async Task Zehn_Treffer_an_einem_Tag_ergeben_einen_Eintrag()
    {
        var anna = Guid.CreateVersion7();

        for (var lauf = 0; lauf < 10; lauf++)
        {
            await Melde(anna);
            _uhr.Weiter(TimeSpan.FromMinutes(30));
        }

        (await Postfach(anna)).GetArrayLength().Should().Be(1);
        _postbote.Gebeten.Should().Equal(anna);
    }

    /// <summary>Am nächsten Tag wieder — sonst wäre es keine Auskunft mehr.</summary>
    [Fact]
    public async Task Am_naechsten_Tag_wieder()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        _uhr.Weiter(TimeSpan.FromDays(1));
        await Melde(anna);

        (await Postfach(anna)).GetArrayLength().Should().Be(2);
    }

    /// <summary>Kurz vor dem Tag noch nicht.</summary>
    /// <remarks>
    /// Die Gegenprobe zur Zeile darüber: ohne sie wäre der Test auch grün, wenn
    /// die Kappe gar nicht griffe und jede Meldung durchginge.
    /// </remarks>
    [Fact]
    public async Task Kurz_vor_dem_Tag_noch_nicht()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        _uhr.Weiter(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        await Melde(anna);

        (await Postfach(anna)).GetArrayLength().Should().Be(1);
    }

    /// <summary>Die Kappe gilt je Art, nicht über alle Arten.</summary>
    /// <remarks>
    /// Eine Marktanfrage am selben Tag muss ankommen. Wäre die Kappe global,
    /// verschluckte eine Entdeckung am Morgen die Lebenslaufanfrage am
    /// Nachmittag — und die ist etwas, worauf jemand antworten soll.
    /// </remarks>
    [Fact]
    public async Task Die_Kappe_gilt_je_Art()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        await Melde(anna, "market_request");
        await Melde(anna);

        var arten = (await Postfach(anna)).EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("kind").GetString())
            .ToArray();

        arten.Should().BeEquivalentTo(["profile_discovered", "market_request"]);
    }

    /// <summary>Die Kappe gilt je Person, nicht für alle zusammen.</summary>
    [Fact]
    public async Task Die_Kappe_gilt_je_Person()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();

        await Melde(anna);
        await Melde(bea);

        (await Postfach(anna)).GetArrayLength().Should().Be(1);
        (await Postfach(bea)).GetArrayLength().Should().Be(1);
    }

    /// <summary>Sie ist einzeln abbestellbar — und das Abbestellen wirkt.</summary>
    /// <remarks>
    /// ADR-0033 verlangt eine <em>eigene</em> Art neben den vieren, gerade damit
    /// sie einzeln abbestellt werden kann: es ist die einzige Auskunft, die eine
    /// Person über ihre eigene Sichtbarkeit bekommt, und wer sie nicht will,
    /// soll nicht alles andere mit abstellen müssen.
    /// </remarks>
    [Fact]
    public async Task Sie_ist_einzeln_abbestellbar()
    {
        var anna = Guid.CreateVersion7();

        var vorher = await Json(
            await AlsPerson(anna).GetAsync(new Uri("/me/notification-preferences", UriKind.Relative)));

        vorher.GetProperty("profile_discovered").GetBoolean().Should().BeTrue(
            "eine neue Art darf nicht stillschweigend ausbleiben");

        var abbestellt = await AlsPerson(anna).PutAsJsonAsync(
            new Uri("/me/notification-preferences", UriKind.Relative),
            new
            {
                resume_request = true,
                market_request = true,
                application_update = true,
                transfer_update = true,
                application_received = true,
                profile_discovered = false
            });

        abbestellt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(abbestellt)).GetProperty("profile_discovered").GetBoolean()
            .Should().BeFalse();

        await Melde(anna);

        _postbote.Gebeten.Should().BeEmpty("abbestellt heisst abbestellt");

        // Der EINTRAG entsteht trotzdem: er liegt hinter der Anmeldung, und ihn
        // wegzulassen hiesse, jemandem zu verschweigen, dass etwas passiert ist.
        // Abbestellt ist die MAIL.
        (await Postfach(anna)).GetArrayLength().Should().Be(1);
    }

    /// <summary>Die Art ist eine von sechs, und alle sechs sind einstellbar.</summary>
    [Fact]
    public void Es_sind_sechs_Arten() =>
        Benachrichtigungsarten.Alle.Should().HaveCount(6)
            .And.Contain(Benachrichtigungsart.ProfileDiscovered);

    /// <summary>Nur diese eine Art trägt eine Tageskappe.</summary>
    /// <remarks>
    /// Die Gegenprobe zur Kappe selbst: wer sie versehentlich auf eine weitere
    /// Art legte, nähme dort den Postfacheintrag mit — und eine Bewerbung, die
    /// sich zweimal am Tag bewegt, verschwände zur Hälfte.
    /// </remarks>
    [Fact]
    public void Nur_die_Entdeckung_traegt_eine_Tageskappe()
    {
        Benachrichtigungsarten.Tageskappe(Benachrichtigungsart.ProfileDiscovered)
            .Should().Be(TimeSpan.FromDays(1));

        foreach (var art in Benachrichtigungsarten.Alle
                     .Where(art => art != Benachrichtigungsart.ProfileDiscovered))
        {
            Benachrichtigungsarten.Tageskappe(art).Should().BeNull(
                $"{Benachrichtigungsarten.Wort(art)} darf so oft kommen, wie es passiert");
        }
    }

    // ---------------------------------------------------------------------

    private HttpClient AlsPerson(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(wer));
        return browser;
    }

    private Task<HttpResponseMessage> Melde(Guid wer, string art = "profile_discovered")
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        return browser.PostAsJsonAsync(
            new Uri("/internal/notifications", UriKind.Relative), new { user_id = wer, kind = art });
    }

    private async Task<JsonElement> Postfach(Guid wer) =>
        await Json(await AlsPerson(wer).GetAsync(new Uri("/notifications/me", UriKind.Relative)));

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;
}
