using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Tests;

/// <summary>A ledger the test steers.</summary>
public sealed class ProbeGate : IConsentGate
{
    /// <summary>Who released their portfolio.</summary>
    public HashSet<Guid> Frei { get; } = [];

    /// <summary>How often it was asked.</summary>
    public int Fragen { get; private set; }

    /// <summary>When true, the ledger says nothing at all.</summary>
    public bool Schweigt { get; set; }

    public Task<bool> MaySeeAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        Fragen++;
        return Schweigt
            ? throw new ConsentGateSilent("der Ledger antwortet im Test nicht")
            : Task.FromResult(Frei.Contains(subject.Value));
    }
}

/// <summary>Writing, being seen, work samples, being erased.</summary>
[Collection(PostgresCollection.Name)]
public class PortfolioJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeGate _tor = new();
    private string _ablage = null!;

    public Task InitializeAsync()
    {
        // A directory of its own per run: the erasure is meant to remove
        // files verifiably, and for that one has to be able to count them.
        _ablage = Path.Combine(Path.GetTempPath(), $"wt-portfolio-{Guid.CreateVersion7():N}");

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:portfolio", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Storage:Wurzel", _ablage);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _tor)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();

        if (Directory.Exists(_ablage))
        {
            Directory.Delete(_ablage, recursive: true);
        }

        return Task.CompletedTask;
    }

    private HttpClient Als(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(
        HttpClient browser, string? attachment = null, string title = "Ein Projekt") =>
        browser.PutAsJsonAsync("/portfolios/me", new
        {
            items = new[]
            {
                new
                {
                    title = title,
                    summary = "Was ich dabei gemacht habe.",
                    url = "https://beispiel.example/projekt",
                    role = "Entwicklerin",
                    year = 2024,
                    attachment = attachment
                }
            }
        });

    /// <summary>
    /// Technologies on a piece of work: <strong>canonicalised and
    /// deduplicated</strong>.
    /// </summary>
    /// <remarks>
    /// The same vocabulary as in the profile and on the CV — "postgres" is
    /// called "PostgreSQL" everywhere (ADR-0023). Canonicalise first,
    /// deduplicate second: the other way round both spellings would stand
    /// there as two entries.
    /// <para>
    /// The reason for the field: it is a place where somebody can NAME what
    /// they can do. It does not make the work searchable — only the profile is
    /// searchable, and a skill gets there with a click.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Technologies_are_canonicalised_and_deduplicated()
    {
        var browser = Als(Guid.CreateVersion7());

        var written = await browser.PutAsJsonAsync("/portfolios/me", new
        {
            items = new[]
            {
                new
                {
                    title = "Ein Projekt",
                    summary = string.Empty,
                    url = (string?)null,
                    role = string.Empty,
                    year = (int?)null,
                    attachment = (string?)null,
                    technologies = new[] { "postgres", "PostgreSQL", " Go ", string.Empty }
                }
            }
        });

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(written)).GetProperty("items")[0]
            .GetProperty("technologies").EnumerateArray()
            .Select(entry => entry.GetString())
            .Should().Equal("PostgreSQL", "Go");
    }

    /// <summary>A piece of work without technologies is not an error, just empty.</summary>
    /// <remarks>
    /// Older rows do not carry the field at all — they are meant to stay
    /// readable without anybody running a follow-up migration.
    /// </remarks>
    [Fact]
    public async Task Without_technologies_the_list_stays_empty()
    {
        var browser = Als(Guid.CreateVersion7());

        var written = await Write(browser);

        (await Json(written)).GetProperty("items")[0]
            .GetProperty("technologies").GetArrayLength().Should().Be(0);
    }

    private async Task<string> LadeHoch(HttpClient browser, string content = "Arbeitsprobe")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "probe.pdf");

        var hochgeladen = await browser.PostAsync("/portfolios/me/attachments", form);
        hochgeladen.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(hochgeladen)).GetProperty("attachment").GetString()!;
    }

    [Fact]
    public async Task Writing_and_reading_ones_own_back()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);

        (await Write(browser)).StatusCode.Should().Be(HttpStatusCode.OK);

        var meins = await Json(await browser.GetAsync("/portfolios/me"));

        meins.GetProperty("items")[0].GetProperty("title").GetString().Should().Be("Ein Projekt");
    }

    /// <summary>
    /// Whether a portfolio may be shown stands only in the ledger. A field here
    /// would be a second truth (ADR-0020 §6).
    /// </summary>
    [Fact]
    public async Task The_portfolio_carries_no_visibility()
    {
        var anna = Guid.CreateVersion7();
        await Write(Als(anna));

        var body = await (await Als(anna).GetAsync("/portfolios/me"))
            .Content.ReadAsStringAsync();

        body.Should().NotContain("visibility");
        body.Should().NotContain("is_public");
    }

    [Fact]
    public async Task Hidden_and_non_existent_answer_the_same()
    {
        var anna = Guid.CreateVersion7();
        var erfunden = Guid.CreateVersion7();
        await Write(Als(anna));

        var browser = Als(Guid.CreateVersion7());

        var beiVerborgenem = await browser.GetAsync($"/portfolios/{anna}");
        var beiErfundenem = await browser.GetAsync($"/portfolios/{erfunden}");

        beiVerborgenem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundenem.StatusCode.Should().Be(beiVerborgenem.StatusCode);
    }

    [Fact]
    public async Task With_a_release_a_stranger_sees_the_portfolio()
    {
        var anna = Guid.CreateVersion7();
        await Write(Als(anna));
        _tor.Frei.Add(anna);

        var read = await Als(Guid.CreateVersion7()).GetAsync($"/portfolios/{anna}");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task When_the_ledger_is_silent_nothing_is_asserted()
    {
        var anna = Guid.CreateVersion7();
        await Write(Als(anna));
        _tor.Frei.Add(anna);
        _tor.Schweigt = true;

        var attempt = await Als(Guid.CreateVersion7()).GetAsync($"/portfolios/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_ledger_is_asked_again_at_every_read()
    {
        var anna = Guid.CreateVersion7();
        await Write(Als(anna));
        _tor.Frei.Add(anna);

        var browser = Als(Guid.CreateVersion7());
        await browser.GetAsync($"/portfolios/{anna}");
        var nachErstem = _tor.Fragen;

        await browser.GetAsync($"/portfolios/{anna}");

        _tor.Fragen.Should().Be(nachErstem + 1, "nichts wird zwischengespeichert");
    }

    /// <summary>
    /// <b>The same consent, no second gate</b> (ADR-0021). A second one would
    /// take effect on the description but not on the work sample.
    /// </summary>
    [Fact]
    public async Task The_attachment_hangs_on_the_same_release_as_the_portfolio()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var attachment = await LadeHoch(ihr);
        await Write(ihr, attachment);

        var fremder = Als(Guid.CreateVersion7());

        var verborgen = await fremder.GetAsync($"/portfolios/{anna}/attachments/{attachment}");
        verborgen.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _tor.Frei.Add(anna);

        var frei = await fremder.GetAsync($"/portfolios/{anna}/attachments/{attachment}");
        frei.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Otherwise a work sample removed from the portfolio would stay reachable
    /// through its name, although it is no longer shown.
    /// </summary>
    [Fact]
    public async Task An_attachment_no_entry_names_is_not_reachable()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var attachment = await LadeHoch(ihr);
        await Write(ihr, attachment);
        _tor.Frei.Add(anna);

        // Take it out of the portfolio without touching the file.
        await Write(ihr, attachment: null);

        var attempt = await Als(Guid.CreateVersion7())
            .GetAsync($"/portfolios/{anna}/attachments/{attachment}");

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Otherwise one could no longer look at one's own work sample after taking
    /// the release back.
    /// </summary>
    [Fact]
    public async Task Ones_own_is_read_without_a_release()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var attachment = await LadeHoch(ihr);
        await Write(ihr, attachment);

        var read = await ihr.GetAsync($"/portfolios/{anna}/attachments/{attachment}");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// An uploaded file a stranger opens is downloaded and not executed in our
    /// origin.
    /// </summary>
    [Fact]
    public async Task An_attachment_is_downloaded_and_not_displayed()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var attachment = await LadeHoch(ihr);
        await Write(ihr, attachment);

        var read = await ihr.GetAsync($"/portfolios/{anna}/attachments/{attachment}");

        read.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        read.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    /// <summary>
    /// The path is formed from SubjectId and a name assigned by the server. A
    /// path inside the name never gets that far.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    public async Task A_path_instead_of_a_name_leads_nowhere(string malicious)
    {
        var anna = Guid.CreateVersion7();
        await Write(Als(anna));
        _tor.Frei.Add(anna);

        var attempt = await Als(Guid.CreateVersion7())
            .GetAsync($"/portfolios/{anna}/attachments/{malicious}");

        attempt.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A portfolio link gets clicked by strangers; javascript: in a field that
    /// ends up in a browser is the ordinary shape of an attack and not an edge
    /// case.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("file:///etc/passwd")]
    public async Task Only_http_and_https_are_allowed(string link)
    {
        var attempt = await Als(Guid.CreateVersion7()).PutAsJsonAsync("/portfolios/me", new
        {
            items = new[] { new { title = "Ein Projekt", url = link } }
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// The row is gone, and so is the work sample. A file still on the disk
    /// afterwards is exactly the silent failure ADR-0027 stands against —
    /// nobody sees it, because the interface no longer links to it.
    /// </summary>
    [Fact]
    public async Task The_erasure_takes_the_files_with_it()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var attachment = await LadeHoch(ihr);
        await Write(ihr, attachment);

        var folder = Path.Combine(_ablage, anna.ToString("N"));
        Directory.Exists(folder).Should().BeTrue("sonst prüft der Test nichts");

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = anna })
        };
        request.Headers.Add("X-Erasure-Secret", Loeschgeheimnis);

        var geloescht = await _dienst.CreateClient().SendAsync(request);

        geloescht.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geloescht)).GetProperty("retained").GetInt32().Should().Be(0);

        var nachher = await ihr.GetAsync("/portfolios/me");
        nachher.StatusCode.Should().Be(HttpStatusCode.OK);
        (await nachher.Content.ReadAsStringAsync()).Trim().Should().Be("null");
        Directory.Exists(folder).Should().BeFalse("die Arbeitsproben sind mitgegangen");
    }

    [Fact]
    public async Task An_empty_erasure_secret_closes_the_endpoint()
    {
        using var ohne = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:portfolio", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Storage:Wurzel", _ablage);
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

    [Fact]
    public async Task More_than_thirty_entries_are_refused()
    {
        var zuViele = Enumerable.Range(0, 31)
            .Select(nummer => new { title = $"Projekt {nummer}" })
            .ToArray();

        var attempt = await Als(Guid.CreateVersion7())
            .PutAsJsonAsync("/portfolios/me", new { items = zuViele });

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
