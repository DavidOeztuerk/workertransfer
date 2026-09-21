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
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Tests;

/// <summary>
/// „Dein Profile wurde entdeckt" — höchstens eine je Person und Tag.
/// </summary>
/// <remarks>
/// <para>Die Auskunft aus ADR-0033, und die einzige Art mit einer
/// <em>eigenen</em> Kappe. Bei jeder anderen entsteht der Postfacheintrag immer
/// und nur die Mail wird gedrosselt: das Inbox liegt hinter der Anmeldung
/// und verrät niemandem etwas.</para>
///
/// <para>Hier ist das anders, und darin liegt der ganze Grund für diese Reihe:
/// ein Eintrag je Hit wäre ein <strong>Zähler über die eigene
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
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Identity:Adresse", "http://identity.test");
            host.UseSetting("Identity:Geheimnis", Meldegeheimnis);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IPostbote>(_ => _postbote));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_uhr));
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

        var inbox = await Inbox(anna);

        inbox.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("kind").GetString().Should().Be("profile_discovered");
    }

    /// <summary>Zehn Hit an einem Tag ergeben EINEN Eintrag.</summary>
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

        (await Inbox(anna)).GetArrayLength().Should().Be(1);
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

        (await Inbox(anna)).GetArrayLength().Should().Be(2);
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

        (await Inbox(anna)).GetArrayLength().Should().Be(1);
    }

    /// <summary>Die Kappe gilt je Art, nicht über alle Arten.</summary>
    /// <remarks>
    /// Eine MarketRequest am selben Tag muss ankommen. Wäre die Kappe global,
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

        var arten = (await Inbox(anna)).EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString())
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

        (await Inbox(anna)).GetArrayLength().Should().Be(1);
        (await Inbox(bea)).GetArrayLength().Should().Be(1);
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
        (await Inbox(anna)).GetArrayLength().Should().Be(1);
    }

    /// <summary>Die Art ist eine von acht, und alle acht sind einstellbar.</summary>
    /// <remarks>
    /// Die achte kam mit assessment-service (ADR-0042):
    /// <c>assessment_update</c>. Diese Zahl steht hier, damit eine neue Art
    /// nicht stillschweigend dazukommt — jede ist ein Schalter, den ein Mensch
    /// zu sehen bekommt, und eine unbemerkt hinzugefügte Art wäre eine Mail,
    /// die niemand bestellt hat.
    /// </remarks>
    [Fact]
    public void Es_sind_acht_Arten() =>
        Benachrichtigungsarten.Alle.Should().HaveCount(8)
            .And.Contain(Benachrichtigungsart.ProfileDiscovered)
            .And.Contain(Benachrichtigungsart.AssessmentUpdate);

    /// <summary>Nur diese eine Art trägt eine Tageskappe.</summary>
    /// <remarks>
    /// Die Gegenprobe zur Kappe selbst: wer sie versehentlich auf eine weitere
    /// Art legte, nähme dort den Postfacheintrag mit — und eine Application, die
    /// sich zweimal am Tag bewegt, verschwände zur Hälfte.
    /// </remarks>
    [Fact]
    public void Nur_die_Entdeckung_traegt_eine_Tageskappe()
    {
        Benachrichtigungsarten.Tageskappe(Benachrichtigungsart.ProfileDiscovered)
            .Should().Be(TimeSpan.FromDays(1));

        foreach (var kind in Benachrichtigungsarten.Alle
                     .Where(kind => kind != Benachrichtigungsart.ProfileDiscovered))
        {
            Benachrichtigungsarten.Tageskappe(kind).Should().BeNull(
                $"{Benachrichtigungsarten.Wort(kind)} darf so oft kommen, wie es passiert");
        }
    }

    // ---------------------------------------------------------------------

    private HttpClient AlsPerson(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(subject));
        return browser;
    }

    private Task<HttpResponseMessage> Melde(Guid subject, string kind = "profile_discovered")
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        return browser.PostAsJsonAsync(
            new Uri("/internal/notifications", UriKind.Relative), new { user_id = subject, kind = kind });
    }

    private async Task<JsonElement> Inbox(Guid subject) =>
        await Json(await AlsPerson(subject).GetAsync(new Uri("/notifications/me", UriKind.Relative)));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
