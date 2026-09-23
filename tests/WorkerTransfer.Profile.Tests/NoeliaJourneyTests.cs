using System.Net;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Noelia's operations surface at the running service — the door and the canary.</summary>
/// <remarks>
/// <para><strong>At the wire and not at the class.</strong> Between the check
/// and what a human reads lie the composition root, the door, the switches and
/// the escaping in the page — four places where a correct finding can become a
/// wrong page. <c>NachweisTests</c> in the Ganzes suite checks the checks; this
/// suite checks the path.</para>
///
/// <para><strong>The canary is the core.</strong> "No value, no key, no name of
/// a person" cannot be established by looking — a page carrying fourteen
/// findings nobody reads line by line, and at the fifteenth even less so. So a
/// value that has no business there is put into the configuration, and then
/// looked for.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class NoeliaJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string Secret = "evidence-secret";
    private const string ErasureSecret = "erasure-secret";

    /// <summary>A key, as somebody deposits it by accident.</summary>
    /// <remarks>
    /// It stands in <c>Draft:Schluessel</c>, so at exactly the place where a real
    /// one would stand, and the AI seam therefore counts as set up. If it turns
    /// up on a page or in the report, that is no cosmetic flaw but a key in a
    /// document somebody passes around.
    /// </remarks>
    private const string Canary = "sk-ant-canary-0815";

    /// <summary>A name, as it must not stand in any operations report.</summary>
    private const string NameCanary = "Jane Doe";

    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", ErasureSecret);
            host.UseSetting("Nachweis:Geheimnis", Secret);

            // The canary sits WHERE A REAL KEY WOULD SIT.
            host.UseSetting("Draft:Schluessel", Canary);
            host.UseSetting("Draft:Adresse", "https://api.anthropic.com/v1/messages");

            // And a name that has no business in a configuration — but that is
            // exactly how it lands there when somebody enters a sender.
            host.UseSetting("Mail:Absender", $"{NameCanary} <jane@example.test>");
            host.UseSetting("environment", "Development");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Noelia's sections — once, so none stays behind.</summary>
    /// <remarks>
    /// Until ADR-0045 seven home-built <c>/nachweis/…</c> addresses stood here.
    /// They are deleted; <c>Noelia.Dashboard</c> delivers the same questions
    /// under <c>/noelia</c>, including <c>report.json</c> from the same reading.
    /// </remarks>
    private static readonly string[] Addresses =
    [
        "/noelia", "/noelia/security", "/noelia/sovereignty", "/noelia/ai",
        "/noelia/obligations", "/noelia/composition", "/noelia/report.json"
    ];

    private HttpClient WithSecret()
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Noelia-Operator", Secret);
        return browser;
    }

    // -------------------------------------------------------------- The door

    /// <summary>Without the secret: 404, not 403.</summary>
    /// <remarks>
    /// <strong>An operations surface whose existence one can guess is itself
    /// already a disclosure</strong> — a 403 would say "there is something here".
    /// </remarks>
    [Theory]
    [InlineData("/noelia")]
    [InlineData("/noelia/security")]
    [InlineData("/noelia/sovereignty")]
    [InlineData("/noelia/ai")]
    [InlineData("/noelia/obligations")]
    [InlineData("/noelia/composition")]
    [InlineData("/noelia/report.json")]
    public async Task Without_the_secret_every_address_answers_404(string path)
    {
        var response = await _service.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            "a 403 would say there is something to fetch here");
    }

    /// <summary>A wrong secret looks like none at all.</summary>
    /// <remarks>
    /// Byte for byte the same answer apart from the correlation id: nobody is to
    /// tell "guessed wrong" from "not wired yet".
    /// </remarks>
    [Fact]
    public async Task A_wrong_secret_answers_like_none_at_all()
    {
        var wrong = _service.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Noelia-Operator", "off-target");

        var withWrong = await wrong.GetAsync("/noelia");
        var without = await _service.CreateClient().GetAsync("/noelia");

        withWrong.StatusCode.Should().Be(HttpStatusCode.NotFound);

        WithoutCorrelation(await withWrong.Content.ReadAsStringAsync())
            .Should().Be(WithoutCorrelation(await without.Content.ReadAsStringAsync()));
    }

    /// <summary>Without a configured secret the door is shut — even with the header.</summary>
    /// <remarks>
    /// <strong>Empty means shut, not open.</strong> Without this test the branch
    /// "secret not configured" runs in no suite, and a patch that then lets
    /// things through would stay green.
    /// </remarks>
    [Fact]
    public async Task Without_a_configured_secret_the_door_is_shut()
    {
        using var withoutDoor = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Nachweis:Geheimnis", "");
            host.UseSetting("environment", "Development");
        });

        var browser = withoutDoor.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Noelia-Operator", Secret);

        foreach (var path in Addresses)
        {
            (await browser.GetAsync(path)).StatusCode
                .Should().Be(
                    HttpStatusCode.NotFound,
                    $"{path}: an empty secret means shut, not open");
        }

        // And the same without the header — otherwise "not set up" would differ
        // by whether somebody presented something.
        (await withoutDoor.CreateClient().GetAsync("/noelia")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A path that names no section answers 404.</summary>
    /// <remarks>
    /// And not the overview under a wrong address: a page that means two things
    /// is a rule nobody remembers at the next endpoint. The two
    /// <c>/nachweis/…</c> paths are the home-built addresses that stood here
    /// until ADR-0045; they stay gone.
    /// </remarks>
    [Theory]
    [InlineData("/noelia/invented")]
    [InlineData("/nachweis/bericht")]
    [InlineData("/nachweis/ki/mehr")]
    public async Task A_path_without_a_section_answers_404(string path) =>
        (await WithSecret().GetAsync(path)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

    // ------------------------------------------------------------- The pages

    /// <summary>With the secret every one of the seven addresses answers.</summary>
    [Theory]
    [InlineData("/noelia", "text/html")]
    [InlineData("/noelia/security", "text/html")]
    [InlineData("/noelia/sovereignty", "text/html")]
    [InlineData("/noelia/ai", "text/html")]
    [InlineData("/noelia/obligations", "text/html")]
    [InlineData("/noelia/composition", "text/html")]
    [InlineData("/noelia/report.json", "application/json")]
    public async Task With_the_secret_every_address_answers(string path, string kind)
    {
        var response = await WithSecret().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(kind);
    }

    /// <summary>The AI page carries the findings this service has.</summary>
    /// <remarks>
    /// Not merely "it answers": an empty page answers too. What is checked is
    /// that the three AI ids stand on it — and that the seam's field set is
    /// really written down, because exactly that is the reason the check exists
    /// beside the test.
    /// </remarks>
    [Fact]
    public async Task The_AI_page_names_the_field_set_of_the_seam()
    {
        // Under `/noelia/security`: Noelia's AI page is fed by the SOVEREIGNTY
        // dependencies, not by checks.
        var page = await WithSecret().GetStringAsync("/noelia/security");

        page.Should().Contain("wt.ki.naht")
            .And.Contain("wt.ki.keine-zahl")
            .And.Contain("wt.ki.anbieter");

        // `wt.ki.protokoll` FELL and therefore no longer stands there: Noelia's
        // `noelia.ai.record-keeping` answers the same question. What Noelia
        // delivers is deleted here and not put beside it.
        page.Should().NotContain("wt.ki.protokoll");
        page.Should().Contain("noelia.ai.record-keeping");

        // And that is the difference that matters: Noelia's inventory
        // recognises model endpoints by HOSTNAME and reports "no recognised
        // endpoint" here; ours reads the configured accesses and names
        // api.anthropic.com. A lower bound against completeness.
        page.Should().Contain("noelia.ai.inventory");

        page.Should().Contain("Headline").And.Contain("Wish");
    }

    /// <summary>
    /// The obligations page has four columns, and none of them is a verdict.
    /// </summary>
    /// <remarks>
    /// The needles stay German and English: the evidence documents are German,
    /// and a verdict column could arrive in either language.
    /// </remarks>
    [Fact]
    public async Task The_obligations_page_has_four_columns_and_no_verdict()
    {
        var page = await WithSecret().GetStringAsync("/noelia/obligations");

        page.Should().Contain("Article")
            .And.Contain("What it asks for")
            .And.Contain("Evidence here")
            .And.Contain("What you still decide");

        foreach (var verdict in new[] { "konform", "zertifi", "compliant" })
        {
            page.Should().NotContain(
                verdict,
                $"\"{verdict}\" would be a verdict column — an article is nothing "
                + "a check can pass");
        }
    }

    /// <summary>And it names the deadline where one applies.</summary>
    /// <remarks>
    /// <strong>A document that presents a duty of 2027 as if it bound today
    /// invites the reader to spend money too early.</strong>
    /// </remarks>
    [Fact]
    public async Task The_obligations_page_names_the_deadlines()
    {
        var page = await WithSecret().GetStringAsync("/noelia/obligations");

        // Annex III is OUR citation, not Noelia's — a library does not know
        // whether its consumer decides about people. That it stands here shows
        // that our checks bring their citations along. The text is German
        // because the evidence documents are.
        page.Should().Contain("Anhang III");

        page.Should().NotContain(
            "nicht hochriskant",
            "the classification is not ours to make");
    }

    // ------------------------------------------------------------ One reading

    /// <summary>Page and report say the same.</summary>
    /// <remarks>
    /// <strong>Two query paths to one statement drift apart, and the first time
    /// nobody notices.</strong> Compared are the ids and the states: that both
    /// come from <em>one</em> reading shows in every finding of the report also
    /// standing on a page.
    /// </remarks>
    [Fact]
    public async Task The_report_and_the_pages_come_from_one_reading()
    {
        var browser = WithSecret();

        var report = JsonDocument.Parse(
            await browser.GetStringAsync("/noelia/report.json")).RootElement;

        var findings = report.GetProperty("securityChecks").GetProperty("results")
            .EnumerateArray().ToList();

        findings.Should().NotBeEmpty("otherwise this test checks nothing");

        var pages = new Dictionary<string, string>
        {
            ["security"] = await browser.GetStringAsync("/noelia/security"),
            ["sovereignty"] = await browser.GetStringAsync("/noelia/sovereignty"),
            ["composition"] = await browser.GetStringAsync("/noelia/composition")
        };

        foreach (var finding in findings)
        {
            var id = finding.GetProperty("id").GetString()!;

            pages.Values.Any(page => page.Contains(id, StringComparison.Ordinal))
                .Should().BeTrue($"\"{id}\" stands in the report but on no page");
        }

        report.GetProperty("service").GetString().Should().Be("profile-service");
        report.GetProperty("schemaVersion").GetInt32().Should().Be(2);
    }

    /// <summary>Every reference in the report carries what stays open.</summary>
    [Fact]
    public async Task No_citation_in_the_report_leaves_the_open_question_empty()
    {
        var report = JsonDocument.Parse(
            await WithSecret().GetStringAsync("/noelia/report.json")).RootElement;

        var references = report.GetProperty("securityChecks").GetProperty("results").EnumerateArray()
            .SelectMany(finding => finding.GetProperty("references").EnumerateArray())
            .ToList();

        references.Should().NotBeEmpty("otherwise this test checks nothing");

        foreach (var reference in references)
        {
            reference.GetProperty("reader").GetString()
                .Should().NotBeNullOrWhiteSpace(
                    $"{reference.GetProperty("citation").GetString()} must say what "
                    + "a human still decides afterwards");
        }
    }

    // ------------------------------------------------------------- The canary

    /// <summary>
    /// No value, no key, no name of a person — checked with a canary, not by
    /// looking.
    /// </summary>
    /// <remarks>
    /// <para>The key stands in this service's configuration, and the AI seam
    /// therefore counts as set up — so the provider finding <em>speaks</em> about
    /// it. It names <c>api.anthropic.com</c>, and that is the purpose; it does
    /// not name the key, and that is the promise.</para>
    ///
    /// <para>All seven addresses are checked at once: a promise that holds for
    /// only six of them is none.</para>
    /// </remarks>
    [Fact]
    public async Task No_key_and_no_name_stands_on_a_page_or_in_the_report()
    {
        var browser = WithSecret();

        foreach (var path in Addresses)
        {
            var content = await browser.GetStringAsync(path);

            content.Should().NotContain(
                Canary, $"{path} carries a deposited key");

            content.Should().NotContain(
                NameCanary, $"{path} carries a human being's name");

            content.Should().NotContain(
                postgres.ConnectionString,
                $"{path} carries a connection string");

            content.Should().NotContain(
                TokenShape.PrivateKey,
                $"{path} carries the private signing key");
        }
    }

    /// <summary>And the canary really is where it belongs.</summary>
    /// <remarks>
    /// <strong>The counter-probe to the test above.</strong> Without it, it would
    /// stay open whether the key is missing because nobody writes it out — or
    /// because nobody ever set it. The inventory names the host, so the finding
    /// speaks about exactly this provider.
    /// </remarks>
    [Fact]
    public async Task The_provider_stands_there_and_only_its_host()
    {
        // Under `/noelia/security`: Noelia's AI page is fed by the SOVEREIGNTY
        // dependencies, not by checks.
        var page = await WithSecret().GetStringAsync("/noelia/security");

        page.Should().Contain("api.anthropic.com");
        page.Should().NotContain("/v1/messages", "the address is not the host");
    }

    private static string WithoutCorrelation(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body, "\"correlationId\"\\s*:\\s*\"[^\"]*\"", "\"correlationId\":\"\"");
}

/// <summary>What <c>/health/ready</c> actually answers for.</summary>
/// <remarks>
/// <para><strong>An empty readiness answers 200.</strong> The address filters
/// the registered checks by the tag <c>ready</c>; without a single one the
/// filtered set is empty, an empty report counts as healthy, and the service
/// reports ready — even over an unreachable database. The orchestrator then
/// sends traffic to a service that answers every request with 500.</para>
///
/// <para>That is why this test checks the <em>content</em> of the answer and not
/// its status: the status is green exactly when nothing is checked.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ReadinessJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Readiness_answers_for_the_database_and_not_for_nothing()
    {
        var response = await _service.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = report.RootElement.GetProperty("checks").EnumerateArray().ToList();

        checks.Should().NotBeEmpty(
            "a readiness without a registration reports 200 over any database");

        // The check name comes from src/shared (`Readiness`) and is German there.
        checks.Should().Contain(
            entry => entry.GetProperty("name").GetString() == "profile-datenbank",
            "the service cannot answer a single request without its database");
    }

    /// <summary>And the description names no value.</summary>
    /// <remarks>
    /// <c>/health/ready</c> answers anybody. An Npgsql exception carries host,
    /// port and sometimes the user — it belongs in the log, not in this answer.
    /// </remarks>
    [Fact]
    public async Task Readiness_names_no_connection_string()
    {
        var content = await _service.CreateClient()
            .GetStringAsync(new Uri("/health/ready", UriKind.Relative));

        content.Should().NotContain(postgres.ConnectionString);
        content.Should().NotContainEquivalentOf("password");
    }
}

/// <summary>Where the framework's key ring lies, and whether it lies there readable.</summary>
/// <remarks>
/// <para>Left alone, ASP.NET writes it into the container's file system and
/// warns twice at startup — two warnings nobody reacts to. Whatever is protected
/// with it stops verifying as soon as the container is replaced.</para>
///
/// <para>Checked here is <em>the same pair</em> that
/// <c>noelia.dataprotection.key-ring</c> reads: a repository and an encryptor.
/// Plus the row in the database, because a configured encryptor that encrypts
/// nothing would look the same from above.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class KeyRingJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-ring");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void The_ring_has_a_repository_and_an_encryptor()
    {
        var options = _service.Services
            .GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull(
            "without a repository the framework writes it into the container's file system");

        options.XmlEncryptor.Should().NotBeNull(
            "a ring that lies unencrypted in a backup is none");
    }

    /// <summary>And no key material stands in plain text in the database.</summary>
    /// <remarks>
    /// The counter-probe to the test above: a configured encryptor does not
    /// prove that it does anything. An unencrypted ring carries
    /// <c>&lt;masterKey&gt;</c> and <c>&lt;value&gt;</c> in plain text. Table,
    /// column and wrapper names come from src/shared (<c>KeyRing</c>) and are
    /// schema.
    /// </remarks>
    [Fact]
    public async Task What_lies_in_the_database_is_encrypted()
    {
        // Protecting forces a key to come into being and be stored.
        _service.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("journey").Protect("something");

        await using var source = ProfileDbContextFactory.DataSource(postgres.ConnectionString);
        await using var command = source.CreateCommand(
            "SELECT inhalt FROM dataprotection_schluessel");
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<string>();

        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        rows.Should().NotBeEmpty("otherwise this test checks an empty table");

        foreach (var row in rows)
        {
            row.Should().NotContain(
                "<masterKey", "that would be the key material in plain text");
            row.Should().Contain(
                "wtGeschuetzterSchluessel", "the wrapper names itself");
        }
    }
}
