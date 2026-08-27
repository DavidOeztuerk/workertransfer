using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Tests;

/// <summary>A ledger the test steers, in place of the real one over HTTP.</summary>
/// <remarks>
/// The ledger is a different service and is not running here. What this suite
/// checks is not how it answers but what <em>this</em> service does with the
/// answer — that it asks at all, on every read, and that a silent ledger is
/// neither a yes nor a no.
/// </remarks>
public sealed class Probetor : IEinwilligungstor
{
    /// <summary>Who has released their profile at all.</summary>
    public HashSet<Guid> ProfilFrei { get; } = [];

    /// <summary>Which (person, company) pairs may read a résumé right now.</summary>
    public HashSet<(Guid Wer, Guid Firma)> LebenslaufFrei { get; } = [];

    /// <summary>How often the gate was asked. The read path must ask every time.</summary>
    public int Fragen { get; private set; }

    /// <summary>When true, the ledger says nothing at all.</summary>
    public bool Schweigt { get; set; }

    public Task<bool> DarfProfilSehenAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        Fragen++;
        return Schweigt
            ? throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht")
            : Task.FromResult(ProfilFrei.Contains(wer.Value));
    }

    public Task<bool> DarfLebenslaufLesenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        Fragen++;
        return Schweigt
            ? throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht")
            : Task.FromResult(LebenslaufFrei.Contains((wer.Value, firma.Value)));
    }

    /// <summary>
    /// The service does not decide the release itself — it asks the ledger to
    /// record it. Here that write is what the answer to a request turns into.
    /// </summary>
    public Task ErteileAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        LebenslaufFrei.Add((wer.Value, firma.Value));
        return Task.CompletedTask;
    }

    /// <inheritdoc cref="ErteileAsync" />
    public Task WiderrufeAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        LebenslaufFrei.Remove((wer.Value, firma.Value));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<bool>> DuerfenLebenslaufLesenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Fragen++;
        return Schweigt
            ? throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht")
            : Task.FromResult<IReadOnlyList<bool>>(
                [.. paare.Select(paar =>
                    LebenslaufFrei.Contains((paar.Wer.Value, paar.Firma.Value)))]);
    }
}

/// <summary>A delivery that records instead of sending.</summary>
public sealed class Probezustellung : IZustellung
{
    public List<(Guid Wer, string Art)> Zugestellt { get; } = [];

    public Task ZustelleAsync(
        SubjectId empfaenger, string art, CancellationToken cancellationToken = default)
    {
        Zugestellt.Add((empfaenger.Value, art));
        return Task.CompletedTask;
    }
}

/// <summary>Writing a résumé, asking for one, answering, taking it back.</summary>
[Collection(PostgresCollection.Name)]
public class LebenslaufreiseTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probetor _tor = new();
    private readonly Probezustellung _versand = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:resume", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor));
                dienste.Replace(ServiceDescriptor.Scoped<IZustellung>(_ => _versand));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsPerson(Guid wer) => Mit(Tokenform.Person(wer));

    private HttpClient AlsFirma(Guid wer, Guid firma) => Mit(Tokenform.Firma(wer, firma));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Schreibe(HttpClient browser) =>
        browser.PutAsJsonAsync("/resumes/me", new
        {
            positions = new[]
            {
                new
                {
                    employer = "Beispiel GmbH",
                    title = "Entwicklerin",
                    started_on = "2020-03",
                    ended_on = (string?)null,
                    description = "Backend"
                }
            },
            education = new[]
            {
                new
                {
                    institution = "TU Beispiel",
                    qualification = "B.Sc.",
                    started_on = "2015-10",
                    ended_on = "2019-09"
                }
            }
        });

    /// <summary>The whole way: write, be asked, say yes, be read, take it back.</summary>
    [Fact]
    public async Task Schreiben_gefragt_werden_erteilen_gelesen_werden_widerrufen()
    {
        var anna = Guid.CreateVersion7();
        var chefin = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();

        var ihr = AlsPerson(anna);
        (await Schreibe(ihr)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Asking needs the PROFILE release, never the existence of a résumé.
        _tor.ProfilFrei.Add(anna);

        var gefragt = await AlsFirma(chefin, firma)
            .PostAsync($"/resumes/{anna}/requests", null);
        gefragt.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await Json(gefragt)).GetProperty("id").GetGuid();

        (await ihr.PostAsync($"/resumes/requests/{id}/grant", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var gelesen = await AlsFirma(chefin, firma).GetAsync($"/resumes/{anna}");
        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await gelesen.Content.ReadAsStringAsync()).Should().Contain("Beispiel GmbH");

        (await ihr.PostAsync($"/resumes/requests/{id}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AlsFirma(chefin, firma).GetAsync($"/resumes/{anna}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// <c>GRANTED</c> means "was granted once", not "holds now". After a
    /// withdrawal the request stays granted and the read comes up empty — that
    /// is the design, not a bug to fix.
    /// </summary>
    [Fact]
    public async Task Nach_dem_Widerruf_bleibt_die_Anfrage_erteilt_und_das_Lesen_leer()
    {
        var anna = Guid.CreateVersion7();
        var chefin = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();

        var ihr = AlsPerson(anna);
        await Schreibe(ihr);
        _tor.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(chefin, firma)
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        await ihr.PostAsync($"/resumes/requests/{id}/grant", null);
        await ihr.PostAsync($"/resumes/requests/{id}/revoke", null);

        var meine = await Json(await ihr.GetAsync("/resumes/me/requests"));

        meine[0].GetProperty("status").GetString().Should().Be(
            "GRANTED", "wurde einmal gewaehrt — das bleibt wahr");
        meine[0].GetProperty("active").GetBoolean().Should().BeFalse(
            "gilt jetzt nicht mehr — und das ist die andere Frage");

        (await AlsFirma(chefin, firma).GetAsync($"/resumes/{anna}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// "Has already written one" is a fact about the person that nobody should
    /// be able to probe for — so asking requires only the profile release.
    /// </summary>
    [Fact]
    public async Task Fragen_geht_auch_ohne_dass_ein_Lebenslauf_existiert()
    {
        var anna = Guid.CreateVersion7();
        _tor.ProfilFrei.Add(anna);

        var gefragt = await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null);

        gefragt.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Without the profile release the company gets the same answer a
    /// non-existent person gets. Anything else would say whether somebody is
    /// here.
    /// </summary>
    [Fact]
    public async Task Ohne_Profilfreigabe_ist_niemand_da()
    {
        var anna = Guid.CreateVersion7();
        var erfunden = Guid.CreateVersion7();
        var browser = AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7());

        await Schreibe(AlsPerson(anna));

        var beiEchter = await browser.PostAsync($"/resumes/{anna}/requests", null);
        var beiErfundener = await browser.PostAsync($"/resumes/{erfunden}/requests", null);

        beiEchter.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundener.StatusCode.Should().Be(beiEchter.StatusCode);
    }

    /// <summary>There is no public switch, so a private person reads nothing.</summary>
    [Fact]
    public async Task Eine_Privatperson_liest_keinen_fremden_Lebenslauf()
    {
        var anna = Guid.CreateVersion7();
        await Schreibe(AlsPerson(anna));

        var versuch = await AlsPerson(Guid.CreateVersion7()).GetAsync($"/resumes/{anna}");

        versuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A withdrawal has to take effect on the very next read, so the gate is
    /// asked every single time and nothing is remembered between requests.
    /// </summary>
    [Fact]
    public async Task Der_Ledger_wird_bei_jedem_Lesen_neu_gefragt()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        await Schreibe(AlsPerson(anna));
        _tor.LebenslaufFrei.Add((anna, firma));

        var browser = AlsFirma(Guid.CreateVersion7(), firma);
        await browser.GetAsync($"/resumes/{anna}");
        var nachErstem = _tor.Fragen;

        await browser.GetAsync($"/resumes/{anna}");

        _tor.Fragen.Should().Be(nachErstem + 1, "nichts wird zwischengespeichert");
    }

    /// <summary>
    /// Neither a 404 nor the résumé: both would assert something nobody knows.
    /// </summary>
    [Fact]
    public async Task Schweigt_der_Ledger_wird_nichts_behauptet()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        await Schreibe(AlsPerson(anna));
        _tor.LebenslaufFrei.Add((anna, firma));
        _tor.Schweigt = true;

        var versuch = await AlsFirma(Guid.CreateVersion7(), firma).GetAsync($"/resumes/{anna}");

        versuch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Only the person asked may answer — and a stranger gets the same 404 a
    /// made-up id gets.
    /// </summary>
    /// <remarks>
    /// Not 403. A foreign request id behaves like a foreign subject id: "not
    /// there" and "not mine" must look the same from outside, or the endpoint
    /// confirms that a particular company asked a particular person.
    /// </remarks>
    [Fact]
    public async Task Ein_Fremder_beantwortet_die_Anfrage_nicht()
    {
        var anna = Guid.CreateVersion7();
        _tor.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        var erfunden = Guid.CreateVersion7();

        var beiFremder = await AlsPerson(Guid.CreateVersion7())
            .PostAsync($"/resumes/requests/{id}/grant", null);
        var beiErfundener = await AlsPerson(Guid.CreateVersion7())
            .PostAsync($"/resumes/requests/{erfunden}/grant", null);

        beiFremder.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundener.StatusCode.Should().Be(beiFremder.StatusCode);
    }

    [Fact]
    public async Task Zweimal_dieselbe_Firma_fragt_nicht_zweimal()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        _tor.ProfilFrei.Add(anna);

        var browser = AlsFirma(Guid.CreateVersion7(), firma);
        await browser.PostAsync($"/resumes/{anna}/requests", null);

        var zweite = await browser.PostAsync($"/resumes/{anna}/requests", null);

        zweite.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A grant and a refusal answer a question the company itself asked, and it
    /// is entitled to hear the answer.
    /// </summary>
    [Fact]
    public async Task Fragen_und_Antworten_landen_in_der_Outbox()
    {
        var anna = Guid.CreateVersion7();
        _tor.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        await AlsPerson(anna).PostAsync($"/resumes/requests/{id}/grant", null);

        await using var kontext = Kontext();
        var arten = kontext.Set<OutboxZeile>().Select(zeile => zeile.Kind).ToList();

        arten.Should().Contain("resume.requested");
        arten.Should().Contain("resume.granted");
    }

    /// <summary>
    /// There is deliberately no kind for a withdrawal. Pushing it at the
    /// company would turn taking a release back into a confrontation, and the
    /// whole point of reading the ledger fresh is that withdrawing costs the
    /// person nothing.
    /// </summary>
    [Fact]
    public async Task Ein_Widerruf_benachrichtigt_niemanden()
    {
        var anna = Guid.CreateVersion7();
        _tor.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        var ihr = AlsPerson(anna);
        await ihr.PostAsync($"/resumes/requests/{id}/grant", null);

        await using var vorher = Kontext();
        var davor = vorher.Set<OutboxZeile>().Count();

        await ihr.PostAsync($"/resumes/requests/{id}/revoke", null);

        await using var nachher = Kontext();
        nachher.Set<OutboxZeile>().Count().Should().Be(davor);
    }

    /// <summary>
    /// The intent commits with the domain change. Rolled back, it is gone too —
    /// so nobody is told their request was answered when it was not.
    /// </summary>
    [Fact]
    public async Task Die_Absicht_liegt_neben_der_Aenderung_in_derselben_Datenbank()
    {
        var anna = Guid.CreateVersion7();
        _tor.ProfilFrei.Add(anna);

        await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null);

        await using var kontext = Kontext();

        kontext.Anfragen.Count(zeile => zeile.SubjectId == anna).Should().Be(1);
        kontext.Set<OutboxZeile>().Count(zeile => zeile.UserId == anna).Should().Be(1);
    }

    private Infrastructure.Persistence.ResumeDbContext Kontext()
    {
        var quelle = Infrastructure.Persistence.ResumeDbContextFactory
            .DataSource(postgres.ConnectionString);

        return new Infrastructure.Persistence.ResumeDbContext(
            (Microsoft.EntityFrameworkCore.DbContextOptions<
                Infrastructure.Persistence.ResumeDbContext>)
            Infrastructure.Persistence.ResumeDbContextFactory.Konfiguriere(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                    Infrastructure.Persistence.ResumeDbContext>(), quelle).Options);
    }
}
