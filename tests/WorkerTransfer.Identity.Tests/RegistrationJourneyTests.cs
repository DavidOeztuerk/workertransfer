using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Records what went out — and whether it could have gone out too early.
/// </summary>
/// <remarks>
/// At send time it asks a <em>separate</em> connection whether the token row is
/// visible. Inside the transaction it would not be, so a mail sent too early is
/// caught here rather than in a comment.
/// </remarks>
public sealed class RecordingMailSender(string connection) : IMailSender
{
    private readonly List<OutgoingMail> _mail = [];

    public IReadOnlyList<OutgoingMail> Sent => _mail;

    /// <summary>Whether every mail found its row already committed.</summary>
    public List<bool> RowWasVisible { get; } = [];

    public async Task SendAsync(
        OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        await using var own = new NpgsqlConnection(connection);
        await own.OpenAsync(cancellationToken);
        await using var command = own.CreateCommand();
        command.CommandText = "SELECT count(*) FROM users WHERE id = @who";
        command.Parameters.AddWithValue("who", mail.Recipient.Value);

        RowWasVisible.Add((long)(await command.ExecuteScalarAsync(cancellationToken))! > 0);
        _mail.Add(mail);
    }
}

/// <summary>The whole way in: register, confirm, sign in.</summary>
[Collection(PostgresCollection.Name)]
public class RegistrationJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";

    private WebApplicationFactory<Program> _service = null!;
    private RecordingMailSender _sender = null!;

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
            host.UseSetting("Mail:WebUrl", "http://localhost:5173");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_sender)));
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

    private static string NewAddress(string givenName) =>
        $"{givenName}-{Guid.NewGuid():N}@company-{Guid.NewGuid():N}.example";

    private Task<HttpResponseMessage> Register(
        string email, string? company = null, string password = Password) =>
        Browser().PostAsJsonAsync("/auth/register", new
        {
            email,
            password = password,
            display_name = "Anna",
            company_name = company
        });

    private Task<HttpResponseMessage> Confirm(string token) =>
        Browser().PostAsJsonAsync("/auth/verify-email", new { token });

    private Task<HttpResponseMessage> SignIn(string email) =>
        Browser().PostAsJsonAsync("/auth/login", new { email, password = Password });

    /// <summary>The token out of the last confirmation mail.</summary>
    private string LastLink() =>
        Regex.Match(
            _sender.Sent.Last(mail => mail.Subject.Contains("bestätige", StringComparison.Ordinal)).Text,
            @"token=(?<token>[A-Za-z0-9_-]+)",
            RegexOptions.None, TimeSpan.FromSeconds(5)).Groups["token"].Value;

    [Fact]
    public async Task Register_confirm_sign_in()
    {
        var email = NewAddress("anna");

        (await Register(email)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Unconfirmed is its own answer, so the interface can offer the mail
        // again instead of leaving somebody in a dead end.
        (await SignIn(email)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Confirm(LastLink())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SignIn(email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Sent inside the transaction, a confirmation link reaches an inbox before
    /// the row it points at exists — and a failed commit leaves somebody with a
    /// link to nothing.
    /// </summary>
    [Fact]
    public async Task The_mail_goes_out_only_after_the_commit()
    {
        await Register(NewAddress("berta"));

        _sender.Sent.Should().NotBeEmpty();
        _sender.RowWasVisible.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task The_confirmation_link_points_at_the_address_the_browser_sees()
    {
        await Register(NewAddress("clara"));

        _sender.Sent.Last().Text.Should().Contain("http://localhost:5173/verify?token=");
    }

    /// <summary>
    /// The same 201 either way. What differs is who gets told, and it is not
    /// the person asking.
    /// </summary>
    [Fact]
    public async Task A_second_registration_warns_the_owner_and_gives_nothing_away()
    {
        var email = NewAddress("dora");

        var firstEntry = await Register(email);
        var second = await Register(email);

        second.StatusCode.Should().Be(firstEntry.StatusCode);
        (await second.Content.ReadAsStringAsync())
            .Should().Be(await firstEntry.Content.ReadAsStringAsync());

        _sender.Sent.Last().Subject.Should().Contain("Registrierungsversuch");
        _sender.Sent.Last().To.Should().Be(email);
    }

    /// <summary>
    /// Otherwise any number of valid links stay in circulation, and the oldest —
    /// possibly the one that went to the wrong mailbox — keeps working.
    /// </summary>
    [Fact]
    public async Task A_resent_link_voids_the_old_one()
    {
        var email = NewAddress("emil");
        await Register(email);
        var older = LastLink();

        var again = await Browser().PostAsJsonAsync("/auth/resend-verification", new { email });
        again.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var newer = LastLink();
        newer.Should().NotBe(older);

        (await Confirm(older)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Confirm(newer)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Always 202, and nothing sent. A different answer would be exactly the
    /// enumeration channel /auth/register is built to close.
    /// </summary>
    [Fact]
    public async Task A_resend_to_an_unknown_address_is_202_as_well()
    {
        var before = _sender.Sent.Count;

        var response = await Browser().PostAsJsonAsync(
            "/auth/resend-verification", new { email = NewAddress("nobody") });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _sender.Sent.Should().HaveCount(before);
    }

    [Fact]
    public async Task The_same_link_twice_is_no_error()
    {
        await Register(NewAddress("frieda"));
        var link = LastLink();

        (await Confirm(link)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Confirm(link)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_invented_link_is_invalid()
    {
        (await Confirm("doesnotexist")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_password_that_is_too_short_is_refused()
    {
        var response = await Register(NewAddress("greta"), password: "short");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>An address that is none creates no account.</summary>
    /// <remarks>
    /// The second half is the more important one: no row may come into being
    /// either. A 422 over a created account would be the same dead end with a
    /// better message.
    /// </remarks>
    [Fact]
    public async Task An_address_that_is_none_creates_no_account()
    {
        var response = await Register("bus");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM users WHERE email = @email";
        command.Parameters.AddWithValue("email", "bus");

        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }

    /// <summary>"Resend" checks the form too.</summary>
    [Fact]
    public async Task A_resend_to_a_malformed_address_is_refused()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            "/auth/resend-verification", new { email = "bus" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_company_comes_into_being_at_the_confirmation_and_not_earlier()
    {
        var email = NewAddress("boss");

        await Register(email, company: "Example GmbH");

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM tenants WHERE domain = @domain";
            command.Parameters.AddWithValue("domain", email.Split('@')[1]);
            ((long)(await command.ExecuteScalarAsync())!).Should().Be(
                0, "before the confirmation it is only an intent");
        }

        var confirmed = await Confirm(LastLink());

        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await confirmed.Content.ReadAsStringAsync()).Should().Contain("Example GmbH");
    }

    /// <summary>
    /// A state that did not exist before: confirmed account, refused
    /// company — and the answer says so instead of showing "all good".
    /// </summary>
    [Fact]
    public async Task A_claimed_domain_confirms_the_account_all_the_same()
    {
        var domain = $"company-{Guid.NewGuid():N}.example";
        var firstEntry = $"boss-{Guid.NewGuid():N}@{domain}";
        var second = $"colleague-{Guid.NewGuid():N}@{domain}";

        await Register(firstEntry, company: "Example GmbH");
        await Confirm(LastLink());

        await Register(second, company: "Second GmbH");
        var response = await Confirm(LastLink());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("domain_already_claimed");
        body.Should().NotContain("Second GmbH");

        // And the account carries: the confirmation is irreversibly right.
        (await SignIn(second)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_freemail_address_cannot_claim_a_company()
    {
        var response = await Register(
            $"boss-{Guid.NewGuid():N}@gmail.com", company: "Example GmbH");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// The ordinary user of a transfer market is a person with no company
    /// at all. The blocklist applies at exactly one place.
    /// </summary>
    [Fact]
    public async Task As_a_private_person_a_freemail_address_is_welcome()
    {
        var response = await Register($"anna-{Guid.NewGuid():N}@gmail.com");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
