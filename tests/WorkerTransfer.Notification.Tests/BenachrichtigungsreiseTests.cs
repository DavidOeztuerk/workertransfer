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
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Tests;

/// <summary>Melden, lesen, abbestellen — und was nie hinausgeht.</summary>
[Collection(PostgresCollection.Name)]
public class BenachrichtigungsreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Meldegeheimnis = "melde-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probepostbote _postbote = new();
    private readonly Probeuhr _uhr = new(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

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

    private HttpClient AlsPerson(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(wer));
        return browser;
    }

    private Task<HttpResponseMessage> Melde(
        Guid wer, string art = "market_request", string geheimnis = Meldegeheimnis)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", geheimnis);
        return browser.PostAsJsonAsync("/internal/notifications", new { user_id = wer, kind = art });
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Der gewöhnliche Weg: gemeldet, im Postfach, Post angestoßen.</summary>
    [Fact]
    public async Task Eine_Meldung_landet_im_Postfach_und_stoesst_Post_an()
    {
        var anna = Guid.CreateVersion7();

        var gemeldet = await Melde(anna);

        gemeldet.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _postbote.Gebeten.Should().Equal(anna);

        var postfach = await Json(await AlsPerson(anna).GetAsync("/notifications/me"));

        postfach.GetArrayLength().Should().Be(1);

        var eintrag = postfach.EnumerateArray().Single();
        eintrag.GetProperty("kind").GetString().Should().Be("market_request");
        eintrag.GetProperty("read_at").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Der Eintrag entsteht immer, die Mail wird gedrosselt.
    /// </summary>
    /// <remarks>
    /// Das Postfach liegt hinter der Anmeldung, wo geprüft wird, wer liest; es
    /// zu drosseln hieße, jemandem zu verschweigen, dass etwas passiert ist.
    /// Die Mail landet in einem Postfach, das nicht nur der Person gehören
    /// muss.
    /// </remarks>
    [Fact]
    public async Task Die_Drossel_haelt_die_Post_auf_nicht_das_Postfach()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna, "market_request");
        await Melde(anna, "resume_request");
        await Melde(anna, "transfer_update");

        _postbote.Gebeten.Should().Equal(anna);

        var postfach = await Json(await AlsPerson(anna).GetAsync("/notifications/me"));

        postfach.GetArrayLength().Should().Be(3);
    }

    /// <summary>Nach der Drosselstunde geht wieder Post hinaus.</summary>
    [Fact]
    public async Task Nach_einer_Stunde_geht_wieder_Post_hinaus()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        _uhr.Weiter(Benachrichtigungswunsch.Drossel);
        await Melde(anna);

        _postbote.Gebeten.Should().Equal(anna, anna);
    }

    /// <summary>Kurz davor noch nicht.</summary>
    [Fact]
    public async Task Kurz_vor_der_Stunde_noch_nicht()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        _uhr.Weiter(Benachrichtigungswunsch.Drossel - TimeSpan.FromMinutes(1));
        await Melde(anna);

        _postbote.Gebeten.Should().Equal(anna);
    }

    /// <summary>Die Drossel gilt je Person, nicht für alle zusammen.</summary>
    [Fact]
    public async Task Die_Drossel_gilt_je_Person()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();

        await Melde(anna);
        await Melde(bea);

        _postbote.Gebeten.Should().Equal(anna, bea);
    }

    /// <summary>Wer nichts eingestellt hat, hat alles an.</summary>
    /// <remarks>
    /// Eine Benachrichtigung über den <em>eigenen</em> Vorgang ist keine
    /// Werbung, sondern die Bedingung dafür, dass „die Person entscheidet"
    /// überhaupt eintreten kann.
    /// </remarks>
    [Fact]
    public async Task Voreingestellt_ist_alles_an()
    {
        var wuensche = await Json(
            await AlsPerson(Guid.CreateVersion7()).GetAsync("/me/notification-preferences"));

        foreach (var feld in new[]
                 {
                     "resume_request", "market_request", "application_update", "transfer_update"
                 })
        {
            wuensche.GetProperty(feld).GetBoolean().Should().BeTrue(feld);
        }
    }

    /// <summary>Abbestellt heißt: keine Post — der Eintrag bleibt trotzdem.</summary>
    [Fact]
    public async Task Abbestellt_heisst_keine_Post_aber_der_Eintrag_bleibt()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);

        await ihr.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = true,
            market_request = false,
            application_update = true,
            transfer_update = true
        });

        await Melde(anna, "market_request");

        _postbote.Gebeten.Should().BeEmpty();

        var postfach = await Json(await ihr.GetAsync("/notifications/me"));

        postfach.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Eine abbestellte Art blockiert die Drossel nicht: die nächste erlaubte
    /// geht hinaus.
    /// </summary>
    [Fact]
    public async Task Eine_abbestellte_Art_verbraucht_die_Drossel_nicht()
    {
        var anna = Guid.CreateVersion7();

        await AlsPerson(anna).PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = true,
            market_request = false,
            application_update = true,
            transfer_update = true
        });

        await Melde(anna, "market_request");
        await Melde(anna, "resume_request");

        _postbote.Gebeten.Should().Equal(anna);
    }

    /// <summary>Die Einstellungen kommen zurück, wie sie geschrieben wurden.</summary>
    [Fact]
    public async Task Die_Einstellungen_bleiben_stehen()
    {
        var ihr = AlsPerson(Guid.CreateVersion7());

        var geschrieben = await ihr.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = false,
            market_request = false,
            application_update = true,
            transfer_update = false
        });

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        var gelesen = await Json(await ihr.GetAsync("/me/notification-preferences"));

        gelesen.GetProperty("resume_request").GetBoolean().Should().BeFalse();
        gelesen.GetProperty("application_update").GetBoolean().Should().BeTrue();
    }

    /// <summary>Gelesen ist gelesen — und ein zweites Mal ändert nichts.</summary>
    [Fact]
    public async Task Alles_gelesen_setzt_den_Zeitpunkt_einmal()
    {
        var anna = Guid.CreateVersion7();
        var ihr = AlsPerson(anna);
        await Melde(anna, "market_request");
        await Melde(anna, "resume_request");

        var gelesen = await Json(await ihr.PostAsync("/notifications/me/read", null));

        gelesen.GetProperty("read").GetInt32().Should().Be(2);

        var postfach = await Json(await ihr.GetAsync("/notifications/me"));

        postfach.EnumerateArray().Should().OnlyContain(
            eintrag => eintrag.GetProperty("read_at").ValueKind != JsonValueKind.Null);

        var nochmal = await Json(await ihr.PostAsync("/notifications/me/read", null));

        nochmal.GetProperty("read").GetInt32().Should().Be(0);
    }

    /// <summary>Das Postfach zeigt nur die eigenen Einträge.</summary>
    [Fact]
    public async Task Das_Postfach_zeigt_nur_die_eigenen_Eintraege()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();
        await Melde(anna);
        await Melde(bea);

        var ihres = await Json(await AlsPerson(anna).GetAsync("/notifications/me"));

        ihres.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Ohne das Geheimnis passiert nichts — und die Antwort ist 404, nicht 401.
    /// </summary>
    /// <remarks>
    /// Ein 401 bestätigt, dass es den Endpunkt gibt.
    /// </remarks>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_ist_es_404()
    {
        var anna = Guid.CreateVersion7();

        var antwort = await Melde(anna, geheimnis: "geraten");

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _postbote.Gebeten.Should().BeEmpty();

        await using var kontext = postgres.Kontext();
        (await kontext.Eingaenge.AnyAsync(zeile => zeile.UserId == anna)).Should().BeFalse();
    }

    /// <summary>
    /// Immer 202 — auch für jemanden, den es hier nie gab.
    /// </summary>
    /// <remarks>
    /// Sonst wäre der Endpunkt ein Orakel darüber, ob es diese Person gibt.
    /// Dieser Dienst weiß es ohnehin nicht: er kennt keine Konten, nur
    /// Kennungen.
    /// </remarks>
    [Fact]
    public async Task Auch_fuer_eine_unbekannte_Kennung_ist_es_202()
    {
        var antwort = await Melde(Guid.CreateVersion7());

        antwort.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Abbestellt und gedrosselt antworten wie zugestellt: 202 und ein leerer
    /// Rumpf.
    /// </summary>
    [Fact]
    public async Task Abbestellt_und_zugestellt_antworten_gleich()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();

        await AlsPerson(bea).PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = false,
            market_request = false,
            application_update = false,
            transfer_update = false
        });

        var zugestellt = await Melde(anna);
        var abbestellt = await Melde(bea);

        zugestellt.StatusCode.Should().Be(abbestellt.StatusCode);
        (await zugestellt.Content.ReadAsStringAsync())
            .Should().Be(await abbestellt.Content.ReadAsStringAsync());
    }

    /// <summary>Eine Art, die es nicht gibt, ist ein Fehler des Aufrufers.</summary>
    [Fact]
    public async Task Eine_erfundene_Art_ist_422()
    {
        var antwort = await Melde(Guid.CreateVersion7(), "irgendwas");

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ohne Anmeldung gibt es kein Postfach.</summary>
    [Fact]
    public async Task Ohne_Token_ist_es_401()
    {
        (await _dienst.CreateClient().GetAsync("/notifications/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _dienst.CreateClient().GetAsync("/me/notification-preferences"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
