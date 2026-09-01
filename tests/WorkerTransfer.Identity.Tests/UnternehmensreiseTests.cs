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

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Founding, inviting, joining, removing — over HTTP against the real schema.
/// </summary>
/// <remarks>
/// Roles are <em>enforced</em> here, which the Python service never did: it
/// checked <c>admin</c> against <c>member</c> nowhere and let the interface hide
/// the entries. A hidden button is not a permission.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class UnternehmensreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Passwort = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private MitschreibenderVersender _versand = null!;

    public async Task InitializeAsync()
    {
        _versand = new MitschreibenderVersender(postgres.ConnectionString);

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Singleton<IVersender>(_versand)));
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

    private string LetzterLink(string betreffteil)
    {
        var text = _versand.Post.Last(p =>
            p.Betreff.Contains(betreffteil, StringComparison.Ordinal)).Text;

        return Regex.Match(text, @"token=(?<token>[A-Za-z0-9_-]+)",
            RegexOptions.None, TimeSpan.FromSeconds(5)).Groups["token"].Value;
    }

    /// <summary>Registers, confirms and signs in — the way in for every actor here.</summary>
    private async Task<HttpClient> Person(string email, string? firma = null)
    {
        var browser = Browser();

        var angelegt = await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Passwort,
            display_name = email.Split('@')[0],
            company_name = firma
        });
        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);

        var bestaetigt = await browser.PostAsJsonAsync(
            "/auth/verify-email", new { token = LetzterLink("bestätige") });
        bestaetigt.StatusCode.Should().Be(HttpStatusCode.OK);

        var angemeldet = await browser.PostAsJsonAsync(
            "/auth/login", new { email, password = Passwort });
        angemeldet.StatusCode.Should().Be(HttpStatusCode.OK);

        return browser;
    }

    private static string Adresse(string name, string domain) =>
        $"{name}-{Guid.NewGuid():N}@{domain}";

    private static string NeueDomain() => $"firma-{Guid.NewGuid():N}.example";

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> ErsteFirma(HttpClient browser)
    {
        var meine = await Json(await browser.GetAsync("/me/companies"));
        return meine[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Wer_gruendet_ist_Administrator_seiner_Firma()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");

        var meine = await Json(await chefin.GetAsync("/me/companies"));

        meine.GetArrayLength().Should().Be(1);
        meine[0].GetProperty("role").GetString().Should().Be("admin");
        meine[0].GetProperty("name").GetString().Should().Be("Beispiel GmbH");
    }

    /// <summary>
    /// The route stays open even though registering can carry the intention:
    /// whoever signs up privately and founds later has no other way in — an
    /// invitation presupposes colleagues who do not exist yet.
    /// </summary>
    [Fact]
    public async Task Wer_sich_privat_registriert_hat_kann_spaeter_gruenden()
    {
        var spaeter = await Person(Adresse("anna", NeueDomain()));

        var gegruendet = await spaeter.PostAsJsonAsync(
            "/companies", new { name = "Spätgründung GmbH" });

        gegruendet.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(gegruendet)).GetProperty("domain").GetString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Eine_Einladung_fuehrt_ueber_die_Adresse_in_die_Firma()
    {
        var domain = NeueDomain();
        var chefin = await Person(Adresse("chefin", domain), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var kollege = Adresse("kollege", "andere.example");
        var eingeladen = await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = kollege, role = "member" });
        eingeladen.StatusCode.Should().Be(HttpStatusCode.Created);

        var browser = await Person(kollege);
        var angenommen = await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LetzterLink("eingeladen") });

        angenommen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(angenommen)).GetProperty("role").GetString().Should().Be("member");

        var meine = await Json(await browser.GetAsync("/me/companies"));
        meine.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// A foreign address is an entirely ordinary case: the domain proves who
    /// owns it, not whom the company may let in.
    /// </summary>
    [Fact]
    public async Task Eine_fremde_Adresse_darf_eingeladen_werden()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var eingeladen = await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations",
            new { email = Adresse("berater", "gmail.com"), role = "member" });

        eingeladen.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Tokens get forwarded, and whoever holds the link is not necessarily who
    /// was invited. The server compares the address; the client asserts nothing.
    /// </summary>
    [Fact]
    public async Task Wer_nicht_eingeladen_war_kommt_mit_dem_Token_nicht_hinein()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations",
            new { email = Adresse("gemeint", "andere.example"), role = "member" });

        var token = LetzterLink("eingeladen");
        var fremder = await Person(Adresse("fremder", "andere.example"));

        var versuch = await fremder.PostAsJsonAsync("/invitations/accept", new { token });

        versuch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Nur_ein_Administrator_darf_einladen()
    {
        var domain = NeueDomain();
        var chefin = await Person(Adresse("chefin", domain), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var kollege = Adresse("kollege", "andere.example");
        await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = kollege, role = "member" });

        var browser = await Person(kollege);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LetzterLink("eingeladen") });

        var versuch = await browser.PostAsJsonAsync(
            $"/companies/{firma}/invitations",
            new { email = Adresse("noch-einer", "andere.example"), role = "member" });

        versuch.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, "Python prueft das nirgends — hier schon");
    }

    /// <summary>
    /// Like a foreign resource: somebody who is not a member must not be able to
    /// tell whether the company exists.
    /// </summary>
    [Fact]
    public async Task Wer_kein_Mitglied_ist_sieht_die_Firma_gar_nicht()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);
        var erfunden = Guid.NewGuid();

        var fremder = await Person(Adresse("fremder", NeueDomain()));

        var beiEchter = await fremder.GetAsync($"/companies/{firma}/members");
        var beiErfundener = await fremder.GetAsync($"/companies/{erfunden}/members");

        beiEchter.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundener.StatusCode.Should().Be(beiEchter.StatusCode);
    }

    /// <summary>
    /// A company without an administrator is not deleted, it is orphaned: the
    /// domain stays claimed, the data stays, and nobody can invite or remove any
    /// more. That dead end appears with one click and can afterwards only be
    /// undone by hand.
    /// </summary>
    [Fact]
    public async Task Der_letzte_Administrator_darf_nicht_gehen()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);
        var wer = (await Json(await chefin.GetAsync("/me"))).GetProperty("user_id").GetString();

        var versuch = await chefin.DeleteAsync($"/companies/{firma}/members/{wer}");

        versuch.StatusCode.Should().Be(
            HttpStatusCode.Conflict, "409, nicht 403 — entfernen darf sie, nur diesen nicht");
    }

    [Fact]
    public async Task Ein_Administrator_entfernt_ein_Mitglied_und_es_verschwindet_aus_der_Liste()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var kollege = Adresse("kollege", "andere.example");
        await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = kollege, role = "member" });

        var browser = await Person(kollege);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LetzterLink("eingeladen") });

        var wer = (await Json(await browser.GetAsync("/me"))).GetProperty("user_id").GetString();

        var entfernt = await chefin.DeleteAsync($"/companies/{firma}/members/{wer}");
        entfernt.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mitglieder = await Json(await chefin.GetAsync($"/companies/{firma}/members"));
        mitglieder.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Eine_zurueckgenommene_Einladung_traegt_nicht_mehr()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var kollege = Adresse("kollege", "andere.example");
        var eingeladen = await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = kollege, role = "member" });
        var token = LetzterLink("eingeladen");
        var id = (await Json(eingeladen)).GetProperty("id").GetString();

        var zurueck = await chefin.DeleteAsync($"/companies/{firma}/invitations/{id}");
        zurueck.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await chefin.GetAsync($"/companies/{firma}/invitations")))
            .GetArrayLength().Should().Be(0);

        var browser = await Person(kollege);
        var versuch = await browser.PostAsJsonAsync("/invitations/accept", new { token });

        versuch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// An id alone would let an administrator of one company act on another
    /// company's invitation.
    /// </summary>
    [Fact]
    public async Task Eine_Einladung_einer_fremden_Firma_laesst_sich_nicht_zuruecknehmen()
    {
        var eine = await Person(Adresse("chefin", NeueDomain()), firma: "Eine GmbH");
        var einesFirma = await ErsteFirma(eine);
        var eingeladen = await eine.PostAsJsonAsync(
            $"/companies/{einesFirma}/invitations",
            new { email = Adresse("kollege", "andere.example"), role = "member" });
        var id = (await Json(eingeladen)).GetProperty("id").GetString();

        var andere = await Person(Adresse("chef", NeueDomain()), firma: "Andere GmbH");
        var anderesFirma = await ErsteFirma(andere);

        var versuch = await andere.DeleteAsync(
            $"/companies/{anderesFirma}/invitations/{id}");

        versuch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Joining changes the memberships, not the running session. An automatic
    /// switch would push somebody out of the company they are working in,
    /// unasked (ADR-0018).
    /// </summary>
    [Fact]
    public async Task Ein_Beitritt_wechselt_die_laufende_Sitzung_nicht()
    {
        var chefin = await Person(Adresse("chefin", NeueDomain()), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var kollege = Adresse("kollege", "andere.example");
        await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = kollege, role = "member" });

        var browser = await Person(kollege);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LetzterLink("eingeladen") });

        var ich = await Json(await browser.GetAsync("/me"));

        ich.TryGetProperty("tenant_id", out var mandant).Should().BeTrue();
        mandant.ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Wer entfernt wird, ist bei der NÄCHSTEN Anfrage draußen — mit demselben
    /// Token.
    /// </summary>
    /// <remarks>
    /// <strong>Der Test, der die ganze Bauart begründet.</strong> Die Rechte
    /// stehen absichtlich nicht im Token: `Mitgliedschaftsrecht` liest die
    /// Mitgliedschaft je Anfrage aus der Tabelle. Lägen sie im Token, wirkte
    /// eine Entfernung erst, wenn es abläuft — und bei genau dieser Handlung
    /// ist sofort das Einzige, was zählt.
    /// <para>
    /// Der Browser der entfernten Person wird hier NICHT neu angemeldet. Genau
    /// das ist der Punkt: dasselbe Token, das eben noch reichte, reicht jetzt
    /// nicht mehr.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Ein_entfernter_Administrator_darf_sofort_nichts_mehr()
    {
        var domain = NeueDomain();
        var chefin = await Person(Adresse("chefin", domain), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        // Eine zweite Person als ADMIN hereinholen.
        var zweite = Adresse("zweite", "andere.example");
        await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = zweite, role = "admin" });

        var browser = await Person(zweite);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LetzterLink("eingeladen") });

        // Sie darf jetzt einladen — der Beleg, dass die Richtlinie ueberhaupt
        // etwas durchlaesst und nicht bloss alles ablehnt.
        var vorher = await browser.PostAsJsonAsync(
            $"/companies/{firma}/invitations",
            new { email = Adresse("dritte", "andere.example"), role = "member" });
        vorher.StatusCode.Should().Be(HttpStatusCode.Created);

        // Die Chefin entfernt sie.
        var wer = await Subjekt(browser);
        var entfernt = await chefin.DeleteAsync($"/companies/{firma}/members/{wer}");
        entfernt.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // DASSELBE Token, kein neues Anmelden.
        var nachher = await browser.PostAsJsonAsync(
            $"/companies/{firma}/invitations",
            new { email = Adresse("vierte", "andere.example"), role = "member" });

        nachher.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "die Rolle wird je Anfrage gelesen, nicht aus dem Token — sonst wirkte "
            + "das Entfernen erst beim Ablauf");
    }

    /// <summary>
    /// Eine Ablehnung durch die Richtlinie trägt dasselbe Fehlerdokument wie
    /// alles andere.
    /// </summary>
    /// <remarks>
    /// Eine Autorisierung wirft nicht, sie schließt die Antwort kurz — ohne
    /// <c>Ablehnungsgestalt</c> fiele hier ein nackter 403 mit leerem Rumpf
    /// heraus, und der Bericht eines Menschen hätte keine
    /// Korrelationskennung.
    /// </remarks>
    [Fact]
    public async Task Die_Ablehnung_traegt_das_uebliche_Fehlerdokument()
    {
        var domain = NeueDomain();
        var chefin = await Person(Adresse("chefin", domain), firma: "Beispiel GmbH");
        var firma = await ErsteFirma(chefin);

        var kollege = Adresse("kollege", "andere.example");
        await chefin.PostAsJsonAsync(
            $"/companies/{firma}/invitations", new { email = kollege, role = "member" });

        var browser = await Person(kollege);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LetzterLink("eingeladen") });

        var versuch = await browser.PostAsJsonAsync(
            $"/companies/{firma}/invitations",
            new { email = Adresse("noch-einer", "andere.example"), role = "member" });

        versuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        versuch.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");

        var rumpf = JsonDocument.Parse(
            await versuch.Content.ReadAsStringAsync()).RootElement;

        rumpf.GetProperty("status").GetInt32().Should().Be(403);
        rumpf.GetProperty("detail").GetString().Should().Be("not permitted");
        rumpf.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();

        // WELCHE Richtlinie fehlte, steht nicht darin — das waere eine
        // Landkarte der Rechte fuer jeden, der sie abfragt.
        rumpf.ToString().Should().NotContain("company.invite");
    }

    /// <summary>Die eigene Subjektkennung, wie sie /me nennt.</summary>
    private static async Task<string> Subjekt(HttpClient browser)
    {
        var antwort = await browser.GetFromJsonAsync<JsonElement>("/me");

        return antwort.GetProperty("user_id").GetString()!;
    }

}
