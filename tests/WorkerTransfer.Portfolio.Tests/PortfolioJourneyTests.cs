using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Tests;

/// <summary>A ledger the test steers.</summary>
public sealed class ProbeGate : IConsentGate
{
    /// <summary>Who released their portfolio.</summary>
    public HashSet<Guid> Released { get; } = [];

    /// <summary>How often it was asked.</summary>
    public int Asked { get; private set; }

    /// <summary>When true, the ledger says nothing at all.</summary>
    public bool Silent { get; set; }

    public Task<bool> MaySeeAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        Asked++;
        return Silent
            ? throw new ConsentGateSilent("the ledger does not answer in the test")
            : Task.FromResult(Released.Contains(subject.Value));
    }
}

/// <summary>Writing, being seen, work samples, being erased.</summary>
[Collection(PostgresCollection.Name)]
public class PortfolioJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeGate _gate = new();
    private string _storageRoot = null!;

    public Task InitializeAsync()
    {
        // A directory of its own per run: the erasure is meant to remove
        // files verifiably, and for that one has to be able to count them.
        _storageRoot = Path.Combine(Path.GetTempPath(), $"wt-portfolio-{Guid.CreateVersion7():N}");

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:portfolio", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Storage:Root", _storageRoot);
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _gate)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private HttpClient As(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(
        HttpClient browser, string? attachment = null, string title = "A project") =>
        browser.PutAsJsonAsync("/portfolios/me", new
        {
            items = new[]
            {
                new
                {
                    title = title,
                    summary = "What I did on it.",
                    url = "https://example.test/project",
                    role = "Developer",
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
        var browser = As(Guid.CreateVersion7());

        var written = await browser.PutAsJsonAsync("/portfolios/me", new
        {
            items = new[]
            {
                new
                {
                    title = "A project",
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
        var browser = As(Guid.CreateVersion7());

        var written = await Write(browser);

        (await Json(written)).GetProperty("items")[0]
            .GetProperty("technologies").GetArrayLength().Should().Be(0);
    }

    private async Task<string> Upload(HttpClient browser, string content = "work sample")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "sample.pdf");

        var uploaded = await browser.PostAsync("/portfolios/me/attachments", form);
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(uploaded)).GetProperty("attachment").GetString()!;
    }

    [Fact]
    public async Task Writing_and_reading_ones_own_back()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);

        (await Write(browser)).StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await Json(await browser.GetAsync("/portfolios/me"));

        mine.GetProperty("items")[0].GetProperty("title").GetString().Should().Be("A project");
    }

    /// <summary>
    /// Whether a portfolio may be shown stands only in the ledger. A field here
    /// would be a second truth (ADR-0020 §6).
    /// </summary>
    [Fact]
    public async Task The_portfolio_carries_no_visibility()
    {
        var anna = Guid.CreateVersion7();
        await Write(As(anna));

        var body = await (await As(anna).GetAsync("/portfolios/me"))
            .Content.ReadAsStringAsync();

        body.Should().NotContain("visibility");
        body.Should().NotContain("is_public");
    }

    [Fact]
    public async Task Hidden_and_non_existent_answer_the_same()
    {
        var anna = Guid.CreateVersion7();
        var invented = Guid.CreateVersion7();
        await Write(As(anna));

        var browser = As(Guid.CreateVersion7());

        var whenHidden = await browser.GetAsync($"/portfolios/{anna}");
        var whenInvented = await browser.GetAsync($"/portfolios/{invented}");

        whenHidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        whenInvented.StatusCode.Should().Be(whenHidden.StatusCode);
    }

    [Fact]
    public async Task With_a_release_a_stranger_sees_the_portfolio()
    {
        var anna = Guid.CreateVersion7();
        await Write(As(anna));
        _gate.Released.Add(anna);

        var read = await As(Guid.CreateVersion7()).GetAsync($"/portfolios/{anna}");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task When_the_ledger_is_silent_nothing_is_asserted()
    {
        var anna = Guid.CreateVersion7();
        await Write(As(anna));
        _gate.Released.Add(anna);
        _gate.Silent = true;

        var attempt = await As(Guid.CreateVersion7()).GetAsync($"/portfolios/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_ledger_is_asked_again_at_every_read()
    {
        var anna = Guid.CreateVersion7();
        await Write(As(anna));
        _gate.Released.Add(anna);

        var browser = As(Guid.CreateVersion7());
        await browser.GetAsync($"/portfolios/{anna}");
        var afterFirst = _gate.Asked;

        await browser.GetAsync($"/portfolios/{anna}");

        _gate.Asked.Should().Be(afterFirst + 1, "nothing is cached");
    }

    /// <summary>
    /// <b>The same consent, no second gate</b> (ADR-0021). A second one would
    /// take effect on the description but not on the work sample.
    /// </summary>
    [Fact]
    public async Task The_attachment_hangs_on_the_same_release_as_the_portfolio()
    {
        var anna = Guid.CreateVersion7();
        var hers = As(anna);
        var attachment = await Upload(hers);
        await Write(hers, attachment);

        var stranger = As(Guid.CreateVersion7());

        var hidden = await stranger.GetAsync($"/portfolios/{anna}/attachments/{attachment}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _gate.Released.Add(anna);

        var released = await stranger.GetAsync($"/portfolios/{anna}/attachments/{attachment}");
        released.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Otherwise a work sample removed from the portfolio would stay reachable
    /// through its name, although it is no longer shown.
    /// </summary>
    [Fact]
    public async Task An_attachment_no_entry_names_is_not_reachable()
    {
        var anna = Guid.CreateVersion7();
        var hers = As(anna);
        var attachment = await Upload(hers);
        await Write(hers, attachment);
        _gate.Released.Add(anna);

        // Take it out of the portfolio without touching the file.
        await Write(hers, attachment: null);

        var attempt = await As(Guid.CreateVersion7())
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
        var hers = As(anna);
        var attachment = await Upload(hers);
        await Write(hers, attachment);

        var read = await hers.GetAsync($"/portfolios/{anna}/attachments/{attachment}");

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
        var hers = As(anna);
        var attachment = await Upload(hers);
        await Write(hers, attachment);

        var read = await hers.GetAsync($"/portfolios/{anna}/attachments/{attachment}");

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
        await Write(As(anna));
        _gate.Released.Add(anna);

        var attempt = await As(Guid.CreateVersion7())
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
        var attempt = await As(Guid.CreateVersion7()).PutAsJsonAsync("/portfolios/me", new
        {
            items = new[] { new { title = "A project", url = link } }
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
        var hers = As(anna);
        var attachment = await Upload(hers);
        await Write(hers, attachment);

        var folder = Path.Combine(_storageRoot, anna.ToString("N"));
        Directory.Exists(folder).Should().BeTrue("otherwise the test checks nothing");

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = anna })
        };
        request.Headers.Add("X-Erasure-Secret", ErasureSecret);

        var erased = await _service.CreateClient().SendAsync(request);

        erased.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(erased)).GetProperty("retained").GetInt32().Should().Be(0);

        var afterwards = await hers.GetAsync("/portfolios/me");
        afterwards.StatusCode.Should().Be(HttpStatusCode.OK);
        (await afterwards.Content.ReadAsStringAsync()).Trim().Should().Be("null");
        Directory.Exists(folder).Should().BeFalse("the work samples went with it");
    }

    /// <summary>The erasure leaves exactly one row behind: THAT it ran.</summary>
    /// <remarks>
    /// Measured in the database, not in the answer: a receipt can say "done"
    /// over an empty trail. Erased twice, because the cascade delivers at least
    /// once — a second delivery must not record a second erasure (ADR-0012,
    /// ADR-0027).
    /// </remarks>
    [Fact]
    public async Task The_erasure_leaves_exactly_one_audit_row()
    {
        var anna = Guid.CreateVersion7();
        var hers = As(anna);
        await Write(hers, await Upload(hers));

        for (var delivery = 0; delivery < 2; delivery++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
            {
                Content = JsonContent.Create(new { user_id = anna })
            };
            request.Headers.Add("X-Erasure-Secret", ErasureSecret);

            (await _service.CreateClient().SendAsync(request))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var source = PortfolioDbContextFactory.DataSource(postgres.ConnectionString);

        await using (var portfolios = source.CreateCommand(
            "SELECT count(*) FROM portfolios WHERE id = $1"))
        {
            portfolios.Parameters.Add(new NpgsqlParameter { Value = anna });
            ((long)(await portfolios.ExecuteScalarAsync())!).Should().Be(0);
        }

        await using var trail = source.CreateCommand(
            "SELECT count(*) FROM audit_events WHERE target_id = $1 AND action = 'erased'");
        trail.Parameters.Add(new NpgsqlParameter { Value = anna });

        ((long)(await trail.ExecuteScalarAsync())!)
            .Should().Be(1, "the trail says once that the erasure ran, and nothing about what it deleted");
    }

    [Fact]
    public async Task An_empty_erasure_secret_closes_the_endpoint()
    {
        using var withoutSecret = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:portfolio", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Storage:Root", _storageRoot);
            host.UseSetting("Erasure:Secret", string.Empty);
            host.UseSetting("environment", "Development");
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        request.Headers.Add("X-Erasure-Secret", "anything");

        (await withoutSecret.CreateClient().SendAsync(request)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task More_than_thirty_entries_are_refused()
    {
        var tooMany = Enumerable.Range(0, 31)
            .Select(number => new { title = $"Project {number}" })
            .ToArray();

        var attempt = await As(Guid.CreateVersion7())
            .PutAsJsonAsync("/portfolios/me", new { items = tooMany });

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
