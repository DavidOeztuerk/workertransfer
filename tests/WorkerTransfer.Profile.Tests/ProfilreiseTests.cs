using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Ein Ledger, den der Test steuert.</summary>
/// <remarks>
/// Der echte läuft hier nicht. Geprüft wird nicht, wie er antwortet, sondern
/// was <em>dieser</em> Dienst mit der Antwort macht — dass er überhaupt fragt,
/// bei jedem Lesen, und dass ein schweigender Ledger weder ein Ja noch ein Nein
/// ist.
/// </remarks>
public sealed class Probetor : IConsentGate
{
    /// <summary>Wer für welches Unternehmen sichtbar ist.</summary>
    public HashSet<(Guid Wer, Guid Firma)> Frei { get; } = [];

    /// <summary>Wie oft gefragt wurde. Der Lesepfad muss jedes Mal fragen.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, sagt der Ledger gar nichts.</summary>
    public bool Schweigt { get; set; }

    public Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Fragen++;
        return Schweigt
            ? throw new ConsentGateSilent("der Ledger antwortet im Test nicht")
            : Task.FromResult(Frei.Contains((subject.Value, company.Value)));
    }
}

/// <summary>Ein Entwerfer, der zählt statt zu fragen.</summary>
public sealed class Probeentwerfer : IDrafter
{
    /// <summary>Was zuletzt hinausgegangen wäre — das ist die interessante Frage.</summary>
    public DraftSituation? Letzte { get; private set; }

    public Task<string> DraftAsync(
        DraftSituation lage, CancellationToken cancellationToken = default)
    {
        Letzte = lage;
        return Task.FromResult("Ich arbeite an verteilten Systemen.");
    }
}

/// <summary>Schreiben, gesehen werden, verborgen bleiben, gelöscht werden.</summary>
[Collection(PostgresCollection.Name)]
public class ProfilreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probetor _tor = new();
    private readonly Probeentwerfer _entwerfer = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _tor));
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => _entwerfer));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsPerson(Guid subject) => Mit(Tokenform.Person(subject));

    private HttpClient AlsFirma(Guid subject, Guid company) => Mit(Tokenform.Firma(subject, company));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(
        HttpClient browser, string ueberschrift = "Entwicklerin") =>
        browser.PutAsJsonAsync("/profiles/me", new
        {
            headline = ueberschrift,
            bio = "Ich arbeite an verteilten Systemen.",
            location = "Berlin",
            remote_ok = true,
            skills = new[] { "C#", "Postgres" }
        });

    [Fact]
    public async Task Schreiben_und_das_eigene_wiederlesen()
    {
        var anna = Guid.CreateVersion7();
        var browser = AlsPerson(anna);

        (await Write(browser)).StatusCode.Should().Be(HttpStatusCode.OK);

        var meins = await Json(await browser.GetAsync("/profiles/me"));

        meins.GetProperty("headline").GetString().Should().Be("Entwicklerin");
        meins.GetProperty("skills").GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// Ob ein PersonProfile gezeigt werden darf, steht ausschließlich im Ledger. Ein
    /// Feld hier wäre eine zweite Wahrheit, und die beiden wären beim ersten
    /// Revocation uneins.
    /// </summary>
    [Fact]
    public async Task Das_Profil_traegt_kein_Sichtbarkeitsfeld()
    {
        var anna = Guid.CreateVersion7();
        await Write(AlsPerson(anna));

        var body = await (await AlsPerson(anna).GetAsync("/profiles/me"))
            .Content.ReadAsStringAsync();

        body.Should().NotContain("visibility");
        body.Should().NotContain("public");
        body.Should().NotContain("is_visible");
    }

    /// <summary>
    /// Verborgen und nicht vorhanden müssen gleich aussehen — bis auf die
    /// Korrelations-Id, die in jedem Problemdokument steht.
    /// </summary>
    [Fact]
    public async Task Verborgen_und_nicht_vorhanden_antworten_gleich()
    {
        var anna = Guid.CreateVersion7();
        var erfunden = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AlsPerson(anna));

        var browser = AlsFirma(Guid.CreateVersion7(), company);

        var beiVerborgener = await browser.GetAsync($"/profiles/{anna}");
        var beiErfundener = await browser.GetAsync($"/profiles/{erfunden}");

        beiVerborgener.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundener.StatusCode.Should().Be(beiVerborgener.StatusCode);

        Ohne_Korrelation(await beiVerborgener.Content.ReadAsStringAsync())
            .Should().Be(Ohne_Korrelation(await beiErfundener.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Mit_Freigabe_sieht_das_Unternehmen_das_Profil()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AlsPerson(anna));
        _tor.Frei.Add((anna, company));

        var gelesen = await AlsFirma(Guid.CreateVersion7(), company).GetAsync($"/profiles/{anna}");

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(gelesen)).GetProperty("headline").GetString().Should().Be("Entwicklerin");
    }

    /// <summary>
    /// Eine Aussage über den Aufrufer, die nichts über die Person verrät, nach
    /// der er fragt.
    /// </summary>
    [Fact]
    public async Task Ohne_aktives_Unternehmen_gibt_es_403_und_nicht_404()
    {
        var anna = Guid.CreateVersion7();
        await Write(AlsPerson(anna));

        var attempt = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/profiles/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Weder 404 noch das PersonProfile: beides behauptete etwas, das niemand weiß.
    /// </summary>
    [Fact]
    public async Task Schweigt_der_Ledger_wird_nichts_behauptet()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AlsPerson(anna));
        _tor.Frei.Add((anna, company));
        _tor.Schweigt = true;

        var attempt = await AlsFirma(Guid.CreateVersion7(), company).GetAsync($"/profiles/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Ein Revocation muss beim nächsten Lesen wirken, also wird jedes Mal neu
    /// gefragt und nichts zwischen Anfragen behalten.
    /// </summary>
    [Fact]
    public async Task Der_Ledger_wird_bei_jedem_Lesen_neu_gefragt()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AlsPerson(anna));
        _tor.Frei.Add((anna, company));

        var browser = AlsFirma(Guid.CreateVersion7(), company);
        await browser.GetAsync($"/profiles/{anna}");
        var nachErstem = _tor.Fragen;

        await browser.GetAsync($"/profiles/{anna}");

        _tor.Fragen.Should().Be(nachErstem + 1, "nichts wird zwischengespeichert");
    }

    /// <summary>
    /// Kein Punktwert, kein Rang, kein Prozentwert — über niemanden (ADR-0022).
    /// </summary>
    [Fact]
    public async Task Niemand_bekommt_eine_Punktzahl()
    {
        var company = Guid.CreateVersion7();
        var anna = Guid.CreateVersion7();
        await Write(AlsPerson(anna));
        _tor.Frei.Add((anna, company));

        var browser = AlsFirma(Guid.CreateVersion7(), company);
        var einzeln = await (await browser.GetAsync($"/profiles/{anna}")).Content.ReadAsStringAsync();
        var eigen = await (await AlsPerson(anna).GetAsync("/profiles/me")).Content.ReadAsStringAsync();

        foreach (var verboten in new[] { "score", "rank", "match", "percent", "weight" })
        {
            einzeln.Should().NotContain(verboten);
            eigen.Should().NotContain(verboten);
        }
    }

    /// <summary>
    /// Diese Klasse IST die Grenze: was die Person über sich geschrieben hat,
    /// und sonst nichts.
    /// </summary>
    [Fact]
    public async Task Der_Entwurf_traegt_nichts_ueber_die_Person_hinaus()
    {
        var anna = Guid.CreateVersion7();
        var browser = AlsPerson(anna);
        await Write(browser);

        var entworfen = await browser.PostAsJsonAsync(
            "/profiles/me/draft", new { wish = "kürzer" });

        entworfen.StatusCode.Should().Be(HttpStatusCode.OK);

        _entwerfer.Letzte.Should().NotBeNull();
        var prompt = _entwerfer.Letzte!.Prompt;

        prompt.Should().NotContain(anna.ToString(), "keine SubjectId");
        prompt.Should().NotContain("@", "keine Adresse");
        prompt.Should().Contain("kürzer", "der Wunsch ist die einzige Zeile des Aufrufers");
    }

    /// <summary>
    /// Der Wunsch ist begrenzt — und ein zu langer ist eine Eingabe, kein Ausfall.
    /// </summary>
    /// <remarks>
    /// Er war das einzige Feld dieses Endpunkts, das an keinem Wertobjekt
    /// vorbeikommt: Überschrift, Text und Fähigkeiten stammen aus dem
    /// gespeicherten PersonProfile und sind dort begrenzt, der Wunsch kam roh aus dem
    /// Rumpf und ging ungeprüft an den fremden Anbieter. Geprüft wird beides —
    /// 501 abgelehnt, 500 durchgelassen: eine Grenze, die auch das Erlaubte
    /// abweist, merkt man erst an einer echten ResumeRequest.
    /// </remarks>
    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.UnprocessableEntity)]
    public async Task Ein_zu_langer_Wunsch_ist_ein_422(int length, HttpStatusCode erwartet)
    {
        var browser = AlsPerson(Guid.CreateVersion7());
        await Write(browser);

        var response = await browser.PostAsJsonAsync(
            "/profiles/me/draft", new { wish = new string('a', length) });

        response.StatusCode.Should().Be(erwartet);
    }

    /// <summary>
    /// Nichts wird gespeichert — nicht der Prompt, nicht die Antwort, und keine
    /// Zeile darüber, dass jemand um Hilfe gebeten hat.
    /// </summary>
    [Fact]
    public async Task Der_Entwurf_hinterlaesst_keine_Spur()
    {
        var anna = Guid.CreateVersion7();
        var browser = AlsPerson(anna);
        await Write(browser);

        await using (var vorher = Kontext())
        {
            var davor = vorher.Pruefeintraege.Count();

            await browser.PostAsJsonAsync("/profiles/me/draft", new { wish = "kürzer" });

            await using var nachher = Kontext();
            nachher.Pruefeintraege.Count().Should().Be(davor);
        }
    }

    [Fact]
    public async Task Die_Loeschung_nimmt_das_Profil_mit()
    {
        var anna = Guid.CreateVersion7();
        await Write(AlsPerson(anna));

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = anna })
        };
        request.Headers.Add("X-Erasure-Secret", Loeschgeheimnis);

        var geloescht = await _dienst.CreateClient().SendAsync(request);

        geloescht.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geloescht)).GetProperty("retained").GetInt32().Should().Be(0);

        var nachher = await AlsPerson(anna).GetAsync("/profiles/me");
        nachher.StatusCode.Should().Be(HttpStatusCode.OK);
        (await nachher.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    [Fact]
    public async Task Ein_leeres_Loeschgeheimnis_schliesst_den_Endpunkt()
    {
        using var ohne = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", string.Empty);
            host.UseSetting("environment", "Development");
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        request.Headers.Add("X-Erasure-Secret", "irgendwas");

        (await ohne.CreateClient().SendAsync(request)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string Ohne_Korrelation(string problem) =>
        System.Text.RegularExpressions.Regex.Replace(
            problem, "\"correlationId\":\"[^\"]*\",?", string.Empty,
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));

    private Infrastructure.Persistence.ProfileDbContext Kontext()
    {
        var source = Infrastructure.Persistence.ProfileDbContextFactory
            .DataSource(postgres.ConnectionString);

        return new Infrastructure.Persistence.ProfileDbContext(
            (Microsoft.EntityFrameworkCore.DbContextOptions<
                Infrastructure.Persistence.ProfileDbContext>)
            Infrastructure.Persistence.ProfileDbContextFactory.Configure(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                    Infrastructure.Persistence.ProfileDbContext>(), source).Options);
    }
}
