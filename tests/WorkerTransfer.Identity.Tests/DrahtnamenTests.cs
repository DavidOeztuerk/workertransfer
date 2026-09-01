using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Die Feldnamen auf dem Draht sind snake_case — auch die zusammengesetzten.
/// </summary>
/// <remarks>
/// <para><strong>Der Fehler, den diese Reihe verhindert, war schon da und
/// niemand sah ihn.</strong> <c>RegisterBody</c> trug als einziger Rumpf der
/// Plattform keine <c>[JsonPropertyName]</c> und fiel damit auf camelCase aus
/// <c>GirderModule.JsonOptions</c> zurück. Die Oberfläche schickt
/// <c>display_name</c>, gebunden wurde <c>displayName</c> — der Wert kam nie an,
/// die Spalte ist <c>NOT NULL</c>, und über die Oberfläche konnte sich
/// <strong>niemand registrieren</strong>.</para>
///
/// <para><strong>Warum 191 grüne Tests das nicht merkten.</strong> Sie schickten
/// selbst camelCase. Eine Reihe, die gegen den Server geschrieben wird statt
/// gegen den Vertrag, bestätigt jeden Dialekt, den der Server gerade spricht —
/// auch einen, den sonst niemand spricht. Deshalb steht hier der Name
/// buchstäblich im JSON und nicht als C#-Eigenschaft: was hier geprüft wird,
/// ist der Draht, und der kennt keine Umbenennung.</para>
///
/// <para><strong>Warum snake_case und nicht camelCase.</strong> Nicht Geschmack,
/// sondern Mehrheit mit Absicht: neun Dienste setzen ihre Vertragsnamen
/// ausdrücklich mit <c>[JsonPropertyName]</c> in snake_case
/// (<c>job_id</c>, <c>shares_resume</c>, <c>created_at</c> …), und die
/// Oberfläche liest und schreibt danach. Identity war die Ausnahme, nicht die
/// Regel.</para>
///
/// <para><strong>Warum es ausgerechnet hier auffiel und sonst nirgends.</strong>
/// Jeder andere Rumpf in identity hat nur einwortige Felder — <c>token</c>,
/// <c>email</c>, <c>name</c>, <c>role</c>. Da sind camelCase und snake_case
/// dasselbe Wort. Erst ein zusammengesetzter Name macht den Unterschied
/// sichtbar, und <c>display_name</c> ist der einzige auf einem Pflichtfeld.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class DrahtnamenTests(Postgres postgres) : IAsyncLifetime
{
    private const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";

    private WebApplicationFactory<Program> _dienst = null!;

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("environment", "Development");
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

    private HttpClient Browser() => _dienst.CreateClient();

    private static string NeueAdresse() =>
        $"draht-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

    /// <summary>
    /// Genau das JSON, das <c>apps/web/src/auth/client.ts</c> schickt, wird
    /// angenommen.
    /// </summary>
    /// <remarks>
    /// Als roher Text und nicht als Objekt: ein anonymes C#-Objekt liefe durch
    /// dieselbe Namensrichtlinie wie der Server und könnte deshalb gar nicht
    /// widersprechen. Genau daran ist es vorbeigegangen.
    /// </remarks>
    [Fact]
    public async Task Der_Rumpf_der_Oberflaeche_wird_angenommen()
    {
        using var inhalt = new StringContent(
            $$"""
            {"email":"{{NeueAdresse()}}","password":"geheim-und-lang-genug",
             "display_name":"Anna"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        var antwort = await Browser().PostAsync("/auth/register", inhalt);

        antwort.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "die Oberflaeche schickt display_name, und der Draht dieser "
            + "Plattform ist snake_case");
    }

    /// <summary>Auch der Firmenname, sonst bricht die Gründung genauso still.</summary>
    [Fact]
    public async Task Auch_der_Firmenname_kommt_an()
    {
        using var inhalt = new StringContent(
            $$"""
            {"email":"{{NeueAdresse()}}","password":"geheim-und-lang-genug",
             "display_name":"Bea","company_name":"Beispiel GmbH"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        (await Browser().PostAsync("/auth/register", inhalt))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// camelCase wird NICHT angenommen — sonst nähme der Dienst beides an und
    /// der Vertrag wäre wieder keiner.
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe zum ersten Test. Ohne sie bliebe er grün, wenn jemand die
    /// Namensrichtlinie auf „egal" stellt — und dann hätte die Plattform wieder
    /// zwei Dialekte, von denen einer irgendwann verschwindet.
    /// </remarks>
    [Fact]
    public async Task camelCase_wird_abgewiesen()
    {
        using var inhalt = new StringContent(
            $$"""
            {"email":"{{NeueAdresse()}}","password":"geheim-und-lang-genug",
             "displayName":"Cara"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        var antwort = await Browser().PostAsync("/auth/register", inhalt);

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var rumpf = await antwort.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        rumpf!["detail"].ToString().Should().Contain(
            "display_name",
            "die Meldung nennt den Namen, den der Aufrufer haette schicken muessen");
    }
}
