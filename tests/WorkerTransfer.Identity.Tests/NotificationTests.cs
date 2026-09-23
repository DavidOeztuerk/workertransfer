using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>A mail sender that only records.</summary>
public sealed class MailRecord : IMailSender
{
    private readonly List<OutgoingMail> _mail = [];

    /// <summary>What would have gone out.</summary>
    public IReadOnlyList<OutgoingMail> Sent => _mail;

    /// <inheritdoc />
    public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        _mail.Add(mail);
        return Task.CompletedTask;
    }
}

/// <summary><c>POST /internal/notify</c> — the only way to an address.</summary>
/// <remarks>
/// notification-service decides <em>whether</em> anything goes out; here stands
/// <em>to whom</em>. What crosses the boundary is nothing but an id — this suite
/// pins that no text and no information can be derived from it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class NotificationTests(Postgres postgres) : IAsyncLifetime
{
    private const string NotifySecret = "notify-secret";
    private const string Password = "secret-and-long-enough";

    private WebApplicationFactory<Program> _service = null!;
    private readonly MailRecord _mail = new();

    public async Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("Notify:Geheimnis", NotifySecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_mail)));
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

    private Task<HttpResponseMessage> Notify(Guid subject, string secret = NotifySecret)
    {
        var browser = Browser();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", secret);
        return browser.PostAsJsonAsync("/internal/notify", new { user_id = subject });
    }

    /// <summary>Creates a confirmed account and answers with its id.</summary>
    private async Task<Guid> ConfirmedAccount()
    {
        var address = $"anna-{Guid.NewGuid():N}@company-{Guid.NewGuid():N}.example";

        await Browser().PostAsJsonAsync("/auth/register", new
        {
            email = address, password = Password, display_name = "Anna"
        });

        var token = Regex.Match(
            _mail.Sent[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;

        await Browser().PostAsJsonAsync("/auth/verify-email", new { token });

        return _mail.Sent[^1].Recipient.Value;
    }

    /// <summary>An unconfirmed account whose address is not proven yet.</summary>
    private async Task<Guid> UnconfirmedAccount()
    {
        var address = $"bea-{Guid.NewGuid():N}@company-{Guid.NewGuid():N}.example";

        await Browser().PostAsJsonAsync("/auth/register", new
        {
            email = address, password = Password, display_name = "Bea"
        });

        return _mail.Sent[^1].Recipient.Value;
    }

    /// <summary>The ordinary path: an id in, a mail out.</summary>
    [Fact]
    public async Task One_id_is_enough_for_the_one_mail()
    {
        var anna = await ConfirmedAccount();
        var before = _mail.Sent.Count;

        var response = await Notify(anna);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _mail.Sent.Should().HaveCount(before + 1);
        _mail.Sent[^1].Recipient.Value.Should().Be(anna);
    }

    /// <summary>
    /// The same subject for every kind — and the kind is not in the body at all.
    /// </summary>
    /// <remarks>
    /// A mail lands in a mailbox that may be the employer's. A line like "Acme
    /// GmbH would like to see your market status" would be exactly the
    /// disclosure this platform is built against.
    /// </remarks>
    [Fact]
    public async Task The_mail_does_not_say_what_it_is_about()
    {
        var anna = await ConfirmedAccount();

        await Notify(anna);

        var mail = _mail.Sent[^1];

        mail.Subject.Should().Be("Neuigkeiten auf WorkerTransfer");
        mail.Text.Should().NotContainAny(
            "Lebenslauf", "Marktstatus", "Bewerbung", "Transfer", "Unternehmen");
        mail.Text.Should().Contain("Melde dich an");
    }

    /// <summary>
    /// An unconfirmed account gets no mail about cases — and the caller learns
    /// nothing of it.
    /// </summary>
    [Fact]
    public async Task An_unconfirmed_account_gets_no_mail()
    {
        var bea = await UnconfirmedAccount();
        var before = _mail.Sent.Count;

        var response = await Notify(bea);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _mail.Sent.Should().HaveCount(before);
    }

    /// <summary>
    /// Always 202 — otherwise the endpoint would be an oracle about membership
    /// of this platform.
    /// </summary>
    [Fact]
    public async Task Known_and_unknown_answer_alike()
    {
        var anna = await ConfirmedAccount();

        var known = await Notify(anna);
        var unknown = await Notify(Guid.CreateVersion7());

        unknown.StatusCode.Should().Be(known.StatusCode);
        (await unknown.Content.ReadAsStringAsync())
            .Should().Be(await known.Content.ReadAsStringAsync());
    }

    /// <summary>Without the secret it is 404, not 401.</summary>
    /// <remarks>
    /// A 401 confirms that the endpoint exists.
    /// </remarks>
    [Fact]
    public async Task Without_the_right_secret_it_is_404()
    {
        var anna = await ConfirmedAccount();
        var before = _mail.Sent.Count;

        var response = await Notify(anna, "guessed");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _mail.Sent.Should().HaveCount(before);
    }
}
