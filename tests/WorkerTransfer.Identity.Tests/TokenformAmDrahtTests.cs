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
/// Der Anspruchssatz, wie er den ENDPUNKT verlässt — nicht wie der Aussteller
/// ihn baut.
/// </summary>
/// <remarks>
/// <strong>Der Unterschied ist genau die Lücke, durch die der neunte Anspruch
/// gerutscht ist.</strong> <see cref="TokenformTests"/> ruft Girders Aussteller
/// direkt und hält seinen Satz fest. Das ist richtig und reicht nicht: zwischen
/// Aussteller und Draht liegen die Einstellungen des Dienstes, seine
/// Verdrahtung und der Keks, in dem das Token schliesslich steht. Ein Anspruch,
/// den erst diese Schicht hinzufügt, wäre dort unsichtbar — und war es: die
/// Zusage in `CLAUDE.md` lautete „acht und nichts sonst", am laufenden Stapel
/// gemessen waren es neun.
///
/// <para>
/// Diese Reihe geht deshalb den ganzen Weg: registrieren, bestätigen, anmelden,
/// auf ein Unternehmen wechseln — und liest das Token dort, wo es der Browser
/// bekommt, nämlich aus dem <c>Set-Cookie</c>. Im Rumpf steht es nie.
/// </para>
///
/// <para>
/// Verglichen wird die MENGE, nicht einzelne Namen. Ein Test, der verbotene
/// Namen aufzählt, bemerkt den zehnten nie, an den niemand gedacht hat.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class TokenformAmDrahtTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    private const string Passwort = "geheim-und-lang-genug";

    /// <summary>Was eine Person trägt, die für niemanden handelt.</summary>
    /// <remarks>
    /// Der neunte — die lange WS-Schreibweise von <c>sub</c> — ist Girders und
    /// bleibt: <c>MapInboundClaims = false</c> schaltet die Ableitung ab, und
    /// siebzehn Leser holen den Aufrufer über diesen Namen, zwei davon in
    /// Fremdpaketen. Ihn zu streichen spart siebzig Byte und macht aus jedem
    /// dieser Leser ein stilles <c>null</c>.
    /// </remarks>
    private static readonly string[] AlsPerson =
    [
        "sub",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
        "email",
        "jti",
        "iat",
        "exp",
        "iss",
        "aud",
        "session_id"
    ];

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

    /// <summary>Der ganze Satz, in beiden Handlungsformen (ADR-0017).</summary>
    [Fact]
    public async Task Am_Draht_traegt_das_Token_genau_diese_Ansprueche()
    {
        var domain = $"firma-{Guid.NewGuid():N}.example";
        var email = $"anna-{Guid.NewGuid():N}@{domain}";

        var browser = _dienst.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        (await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Passwort,
            display_name = "Anna",
            company_name = "Beispiel GmbH"
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await browser.PostAsJsonAsync("/auth/verify-email", new
        {
            token = Regex.Match(
                _versand.Post.Last(p => p.Betreff.Contains("bestätige", StringComparison.Ordinal)).Text,
                @"token=(?<token>[A-Za-z0-9_-]+)", RegexOptions.None, TimeSpan.FromSeconds(5))
                .Groups["token"].Value
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var angemeldet = await browser.PostAsJsonAsync(
            "/auth/login", new { email, password = Passwort });
        angemeldet.StatusCode.Should().Be(HttpStatusCode.OK);

        // ALS PERSON: kein Mandant. `null` heisst „handelt für sich selbst"
        // und nicht „fehlt" — deshalb steht der Anspruch gar nicht erst da.
        Ansprueche(angemeldet).Should().BeEquivalentTo(AlsPerson);

        var meine = JsonDocument.Parse(
            await (await browser.GetAsync("/me/companies")).Content.ReadAsStringAsync())
            .RootElement;

        var gewechselt = await browser.PostAsync(
            $"/auth/company/{meine[0].GetProperty("id").GetString()}", null);
        gewechselt.StatusCode.Should().Be(HttpStatusCode.OK);

        // FÜR EINE FIRMA: genau EINER kommt dazu. Nicht `tenant_id`, nicht
        // `roles` — die Mitgliedschaft entscheidet je Anfrage (ADR-0018).
        Ansprueche(gewechselt).Should().BeEquivalentTo([.. AlsPerson, "tenant"]);
    }

    /// <summary>Im RUMPF steht das Token nie.</summary>
    /// <remarks>
    /// Es geht ausschliesslich als <c>httpOnly</c>-Keks hinaus, damit kein
    /// Skript im Browser es lesen kann. Ein Feld daneben im Rumpf wäre ein
    /// zweiter Weg an dieselbe Zeichenkette — und der erste, den jemand
    /// versehentlich in ein Protokoll schreibt.
    /// </remarks>
    [Fact]
    public async Task Das_Token_steht_nicht_im_Rumpf()
    {
        var email = $"bo-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

        var browser = _dienst.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Passwort,
            display_name = "Bo"
        });

        await browser.PostAsJsonAsync("/auth/verify-email", new
        {
            token = Regex.Match(
                _versand.Post.Last(p => p.Betreff.Contains("bestätige", StringComparison.Ordinal)).Text,
                @"token=(?<token>[A-Za-z0-9_-]+)", RegexOptions.None, TimeSpan.FromSeconds(5))
                .Groups["token"].Value
        });

        var angemeldet = await browser.PostAsJsonAsync(
            "/auth/login", new { email, password = Passwort });

        var rumpf = await angemeldet.Content.ReadAsStringAsync();

        rumpf.Should().NotContain("access_token");
        rumpf.Should().NotContain(Keks(angemeldet));
    }

    /// <summary>Die Anspruchsnamen aus dem <c>access</c>-Keks der Antwort.</summary>
    private static IEnumerable<string> Ansprueche(HttpResponseMessage antwort)
    {
        var rumpf = Keks(antwort).Split('.')[1];

        return JsonDocument
            .Parse(Convert.FromBase64String(
                rumpf.Replace('-', '+').Replace('_', '/')
                    .PadRight(rumpf.Length + ((4 - (rumpf.Length % 4)) % 4), '=')))
            .RootElement.EnumerateObject().Select(feld => feld.Name);
    }

    /// <summary>Das Token, gelesen wie ein Browser es bekommt.</summary>
    private static string Keks(HttpResponseMessage antwort)
    {
        antwort.Headers.TryGetValues("Set-Cookie", out var kekse).Should().BeTrue(
            "das Token verlässt den Dienst ausschliesslich als Keks");

        var zugriff = kekse!.FirstOrDefault(k =>
            k.StartsWith("access=", StringComparison.Ordinal));

        zugriff.Should().NotBeNull("ohne `access` gäbe es keine Sitzung");

        return zugriff!["access=".Length..].Split(';')[0];
    }
}
