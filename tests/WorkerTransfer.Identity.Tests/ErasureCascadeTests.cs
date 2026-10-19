using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>What the test decides, shared across every scope.</summary>
/// <remarks>
/// Separate from the delivery itself because the delivery is scoped: it holds a
/// database context, and one kept past its scope is a disposed one. Caching the
/// wrapper across passes is exactly that mistake, and it shows up as
/// <c>ObjectDisposedException</c> in a warning nobody reads.
/// </remarks>
public sealed class TestControl
{
    /// <summary>Recipients that refuse. Everything else goes through.</summary>
    public HashSet<string> Dead { get; } = new(StringComparer.Ordinal);

    /// <summary>What went out, in order.</summary>
    public List<string> Delivered { get; } = [];
}

/// <summary>A delivery that answers the way a test tells it to.</summary>
public sealed class TestRecipient(TestControl control, IDelivery real) : IDelivery
{
    public async Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        if (control.Dead.Contains(kind))
        {
            throw new HttpRequestException($"{kind} does not answer");
        }

        // The real one for the two local kinds — they carry the order and the
        // final step, and faking them would fake the thing under test. The
        // foreign recipients are HTTP calls to services that do not run here.
        if (kind is ErasureRecipient.FinalNotice or ErasureRecipient.Identity)
        {
            await real.DeliverAsync(recipient, kind, cancellationToken);
        }

        control.Delivered.Add(kind);
    }
}

/// <summary>The cascade: order, completeness, and what a dead recipient does.</summary>
[Collection(PostgresCollection.Name)]
public class ErasureCascadeTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";

    private WebApplicationFactory<Program> _service = null!;
    private RecordingMailSender _sender = null!;
    private readonly TestControl _control = new();

    public async Task InitializeAsync()
    {
        _sender = new RecordingMailSender(postgres.ConnectionString);

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "probe-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_sender));
                services.AddSingleton(_control);
                services.Replace(ServiceDescriptor.Scoped<IDelivery>(provider =>
                    new TestRecipient(
                        _control,
                        ActivatorUtilities.CreateInstance<
                            WorkerTransfer.Identity.Infrastructure.Erasure
                                .HttpErasureDelivery>(provider))));
            });
        });

        using var scope = _service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Browser() =>
        _service.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<(HttpClient Browser, Guid Subject, string Email)> Person()
    {
        var browser = Browser();
        var email = $"anna-{Guid.NewGuid():N}@company-{Guid.NewGuid():N}.example";

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email, password = Password, display_name = "Anna"
        });

        var token = System.Text.RegularExpressions.Regex.Match(
            _sender.Sent.Last().Text, @"token=(?<t>[A-Za-z0-9_-]+)",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5)).Groups["t"].Value;

        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email, password = Password });

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM users WHERE email = @email";
        command.Parameters.AddWithValue("email", email);

        return (browser, (Guid)(await command.ExecuteScalarAsync())!, email);
    }

    private async Task<List<(string Kind, DateTime? Delivered)>> Rows(Guid subject)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT kind, delivered_at FROM outbox WHERE user_id = @who ORDER BY created_at";
        command.Parameters.AddWithValue("who", subject);

        var rows = new List<(string, DateTime?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDateTime(1)));
        }

        return rows;
    }

    private async Task<string?> AccountStatusValue(Guid subject)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status::text FROM users WHERE id = @who";
        command.Parameters.AddWithValue("who", subject);

        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// One pass per scope, the way the loop does it. Several passes because the
    /// order is enforced: the notice waits for the recipients, the account
    /// waits for the notice.
    /// </summary>
    private async Task Pass(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            using var scope = _service.Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<OutboxDispatcher<IdentityDbContext>>()
                .PassAsync();
        }
    }

    [Fact]
    public async Task The_request_locks_immediately_and_writes_thirteen_intents()
    {
        var (browser, subject, _) = await Person();

        var requested = await browser.PostAsync("/account/erasure", null);

        requested.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await AccountStatusValue(subject)).Should().Be("disabled", "from now on nothing happens under this name");

        var rows = await Rows(subject);
        rows.Should().HaveCount(13, "eleven recipients, the closing notice and identity itself");
        rows.Select(row => row.Kind).Should().Contain(RequestErasureHandler.Intents);
    }

    /// <summary>
    /// Who is a recipient is decided by one rule: does the service hold a row
    /// per person. github is gone with its service (ADR-0022 deleted
    /// worker-github because it scored people); notification took its place
    /// because it holds the preferences, keyed by the person.
    /// </summary>
    [Fact]
    public void A_recipient_is_whoever_holds_a_row_per_human_being()
    {
        ErasureRecipient.Services.Should().BeEquivalentTo(
        [
            "consent", "profile", "resume", "portfolio",
            "applications", "transfer", "github", "notification", "scout", "advisor",
            "assessment"
        ]);

        // `github` belongs on it: the service holds one row per human being —
        // the link "this platform person is that GitHub name". It once did not
        // stand on this list, because the deleted PACKAGE `worker-github` (which
        // evaluated people) was confused with the SERVICE built under ADR-0022,
        // which does the opposite.
        ErasureRecipient.Services.Should().Contain("github");

        // `scout` joined with its first table (ADR-0036). It holds stored
        // SEARCHES — the filters, never a result — and each belongs to the human
        // being who filed it; plus outgoing rows that are about a human being.
        // Whoever holds one row per human being is a recipient: that is the
        // whole rule.
        ErasureRecipient.Services.Should().Contain("scout");

        // `advisor` joined with its first table (ADR-0037). It holds a MANDATE
        // whose key IS the person — entry month, salary range, workload,
        // excluded companies — and the conversations they stand in. Visibility
        // it expressly does not hold; that falls at consent-service. Whoever
        // holds one row per human being is a recipient: that is the whole
        // rule.
        ErasureRecipient.Services.Should().Contain("advisor");

        // `assessment` joined with its first table (ADR-0042). It holds the
        // tasks set to a human being, what they submitted and the one reply to
        // it — all under their id. AND NO RETENTION CASE, not even for the
        // evaluation: it is no record belonging to somebody else, and one that
        // survived an erasure would be exactly the reference ADR-0042 rules
        // out.
        ErasureRecipient.Services.Should().Contain("assessment");

        ErasureRecipient.Services.Should().NotContain("jobs", "it holds nothing about a person");
        ErasureRecipient.Services.Should().NotContain("companies");
    }

    [Fact]
    public async Task After_the_request_the_sign_in_no_longer_carries()
    {
        var (browser, _, email) = await Person();

        await browser.PostAsync("/account/erasure", null);

        var again = await Browser().PostAsJsonAsync(
            "/auth/login", new { email, password = Password });

        again.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Pressing twice is not an error, and the second press must not start a
    /// second cascade: eight more rows would be eight more deliveries for a
    /// thing that happens once.
    /// </summary>
    [Fact]
    public async Task Requesting_twice_produces_no_second_cascade()
    {
        var (browser, subject, _) = await Person();

        await browser.PostAsync("/account/erasure", null);
        var second = await browser.PostAsync("/account/erasure", null);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Rows(subject)).Should().HaveCount(13);
    }

    /// <summary>
    /// A message saying "everything is gone" while a service still holds data
    /// is the one lie this whole cascade exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_dead_recipient_blocks_the_closing_notice_and_the_account()
    {
        var (browser, subject, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        _control.Dead.Add(ErasureRecipient.Kind("resume"));

        await Pass(3);

        var rows = await Rows(subject);

        rows.Single(row => row.Kind == ErasureRecipient.Kind("resume")).Delivered
            .Should().BeNull("the recipient does not answer");
        rows.Single(row => row.Kind == ErasureRecipient.FinalNotice).Delivered
            .Should().BeNull("it may go out only once everybody has acknowledged");
        rows.Single(row => row.Kind == ErasureRecipient.Identity).Delivered
            .Should().BeNull("and the account falls last");

        (await AccountStatusValue(subject)).Should().Be("disabled", "it still stands, but disabled");
    }

    /// <summary>
    /// Orderly waiting is not a failure. Without its own answer the enforced
    /// order would look exactly like a broken recipient — and under an attempt
    /// ceiling it would strangle the delivery.
    /// </summary>
    [Fact]
    public async Task Orderly_waiting_spends_no_attempt()
    {
        var (browser, subject, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        _control.Dead.Add(ErasureRecipient.Kind("consent"));
        await Pass(3);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT attempts, last_error FROM outbox WHERE user_id = @who AND kind = @kind";
        command.Parameters.AddWithValue("who", subject);
        command.Parameters.AddWithValue("kind", ErasureRecipient.FinalNotice);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(0, "waiting is no failure");
        reader.GetString(1).Should().BeEmpty();
    }

    [Fact]
    public async Task When_everybody_has_acknowledged_the_account_falls_and_the_mail_goes_out()
    {
        var (browser, subject, email) = await Person();
        await browser.PostAsync("/account/erasure", null);

        await Pass(3);

        var rows = await Rows(subject);
        rows.Should().OnlyContain(row => row.Delivered != null, "all thirteen are through");

        (await AccountStatusValue(subject)).Should().BeNull("the row is gone");

        var closing = _sender.Sent.Last(mail => mail.To == email);
        closing.Subject.Should().Contain("gelöscht");
        // Says only THAT it is done. Listing what was deleted would copy the
        // data into an inbox that may not be the person's alone.
        closing.Text.Should().NotContain("profile").And.NotContain("resume");
    }

    /// <summary>
    /// The proof is the set of rows, and it is a query rather than a guess.
    /// </summary>
    [Fact]
    public async Task Open_means_open_and_empty_means_finished()
    {
        var (browser, subject, _) = await Person();
        await browser.PostAsync("/account/erasure", null);

        using var scope = _service.Services.CreateScope();
        var erasureLedger = scope.ServiceProvider.GetRequiredService<IErasureLedger>();

        (await erasureLedger.OpenIntentsAsync(new SubjectId(subject))).Should().HaveCount(13);

        await Pass(3);

        (await erasureLedger.OpenIntentsAsync(new SubjectId(subject))).Should().BeEmpty();
    }

    /// <summary>
    /// A company left without an administrator is put dormant, not deleted: it
    /// is not a natural person, and its adverts are still its own. The intent
    /// deliberately carries no erasure prefix, so a silent jobs-service cannot
    /// hold a person's erasure open.
    /// </summary>
    [Fact]
    public async Task The_last_administrator_puts_her_company_dormant()
    {
        var browser = Browser();
        var domain = $"company-{Guid.NewGuid():N}.example";
        var email = $"boss-{Guid.NewGuid():N}@{domain}";

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email, password = Password, display_name = "Boss", company_name = "Example GmbH"
        });
        var token = System.Text.RegularExpressions.Regex.Match(
            _sender.Sent.Last().Text, @"token=(?<t>[A-Za-z0-9_-]+)",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5)).Groups["t"].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email, password = Password });

        await browser.PostAsync("/account/erasure", null);
        await Pass(3);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM tenants WHERE domain = @domain";
        command.Parameters.AddWithValue("domain", domain);

        (await command.ExecuteScalarAsync()).Should().Be("dormant");
    }
}
