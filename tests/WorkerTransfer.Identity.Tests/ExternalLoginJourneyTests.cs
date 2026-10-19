using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Application.SignIn;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Signing in with Google, Microsoft, Apple (ADR-0053) — the whole way through
/// the service, against a provider that answers what the test says.
/// </summary>
/// <remarks>
/// The one rule the suite exists for: <strong>linked by the provider's id,
/// never by the address.</strong> Whoever can enter a foreign address at a
/// provider must not get into the account behind it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ExternalLoginJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";
    private const string Web = "http://localhost:5173";

    private WebApplicationFactory<Program> _service = null!;
    private readonly MailRecord _mail = new();
    private readonly ProviderProbe _provider = new();

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
            host.UseSetting("Mail:WebUrl", Web);
            host.UseSetting("Notify:Secret", "notify-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_mail));
                services.Replace(ServiceDescriptor.Scoped<IExternalProviders>(_ => _provider));
            });
        });

        using var scope = _service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    // https, because Apple's flow cookie is Secure — as in the browser.
    private HttpClient Browser() =>
        _service.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static string Unique(string domain = "gmail.com") => $"anna.{Guid.NewGuid():N}@{domain}";

    private static ExternalIdentity Person(
        ExternalProvider provider, string email, bool verified = true, string? subject = null) =>
        new(provider, subject ?? Guid.NewGuid().ToString("N"), email, verified, "Anna", "Beispiel", null);

    /// <summary>Starts a flow and comes back with the state the provider would send.</summary>
    private async Task<HttpResponseMessage> RoundTrip(
        HttpClient browser, string provider, ExternalIdentity identity, string intent = "signin", string? state = null)
    {
        var start = await browser.GetAsync($"/auth/external/{provider}/start?intent={intent}");
        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var sent = QueryHelpers.ParseQuery(start.Headers.Location!.Query);

        _provider.Next = identity;

        if (provider == "apple")
        {
            return await browser.PostAsync($"/auth/external/{provider}/callback", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["code"] = "the-code",
                    ["state"] = state ?? sent["state"].ToString(),
                    ["user"] = """{"name":{"firstName":"Anna","lastName":"Apfel"},"email":"x"}"""
                }));
        }

        return await browser.GetAsync(
            $"/auth/external/{provider}/callback?code=the-code&state={Uri.EscapeDataString(state ?? sent["state"].ToString())}");
    }

    private async Task<T> Database<T>(Func<IdentityDbContext, Task<T>> read)
    {
        using var scope = _service.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    private async Task<(HttpClient Browser, Guid Subject, string Email)> PasswordAccount(string? email = null)
    {
        var address = email ?? Unique("company.example");
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new { email = address, password = Password, display_name = "Anna" });
        var token = Regex.Match(_mail.Sent[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = address, password = Password });

        var session = JsonDocument.Parse(await (await browser.GetAsync("/auth/session")).Content.ReadAsStringAsync());
        return (browser, session.RootElement.GetProperty("user").GetProperty("user_id").GetGuid(), address);
    }

    [Fact]
    public async Task Start_sends_the_browser_to_the_provider_with_pkce_and_a_sealed_flow()
    {
        var browser = Browser();

        var start = await browser.GetAsync("/auth/external/google/start");

        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        start.Headers.Location!.Host.Should().Be("provider.test");
        var cookie = start.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("external_flow=", StringComparison.Ordinal));
        cookie.Should().Contain("httponly").And.Contain("path=/auth/external").And.Contain("samesite=lax");

        // The verifier stays in the sealed cookie; only its challenge leaves.
        _provider.Next = Person(ExternalProvider.Google, Unique());
        var sent = QueryHelpers.ParseQuery(start.Headers.Location.Query);
        await browser.GetAsync($"/auth/external/google/callback?code=c&state={Uri.EscapeDataString(sent["state"]!)}");

        HttpExternalProviders.Challenge(_provider.LastVerifier!).Should().Be(sent["challenge"].ToString());
        _provider.LastNonce.Should().Be(sent["nonce"].ToString());
        cookie.Should().NotContain(_provider.LastVerifier!);
    }

    [Fact]
    public async Task An_unknown_provider_or_one_without_credentials_has_no_door()
    {
        (await Browser().GetAsync("/auth/external/myspace/start")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        _provider.Off.Add(ExternalProvider.Apple);
        (await Browser().GetAsync("/auth/external/apple/start")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var buttons = JsonDocument.Parse(await (await Browser().GetAsync("/auth/external")).Content.ReadAsStringAsync());
        buttons.RootElement.GetProperty("providers").EnumerateArray().Select(entry => entry.GetString())
            .Should().Equal("google", "microsoft");
    }

    [Fact]
    public async Task A_proven_new_address_becomes_an_active_person_and_is_signed_in()
    {
        var browser = Browser();
        var email = Unique();

        var back = await RoundTrip(browser, "google", Person(ExternalProvider.Google, email));

        back.Headers.Location!.ToString().Should().Be($"{Web}/overview");
        var session = JsonDocument.Parse(await (await browser.GetAsync("/auth/session")).Content.ReadAsStringAsync()).RootElement;
        session.GetProperty("user").GetProperty("email").GetString().Should().Be(email);

        var row = await Database(context => context.Users.SingleAsync(user => user.Email == email));
        row.PasswordHash.Should().BeEmpty("no password was ever set");
        (await Database(context => context.ExternalLogins.CountAsync(login => login.UserId == row.Id))).Should().Be(1);
        (await Database(context => context.Memberships.CountAsync(member => member.UserId == row.Id)))
            .Should().Be(0, "a provider makes a person, never a company (ADR-0019)");
    }

    [Fact]
    public async Task The_same_provider_id_comes_back_into_the_same_account()
    {
        var email = Unique();
        var subject = Guid.NewGuid().ToString("N");

        await RoundTrip(Browser(), "google", Person(ExternalProvider.Google, email, subject: subject));
        var again = await RoundTrip(Browser(), "google", Person(ExternalProvider.Google, email, subject: subject));

        again.Headers.Location!.ToString().Should().Be($"{Web}/overview");
        (await Database(context => context.Users.CountAsync(user => user.Email == email))).Should().Be(1);
    }

    /// <summary>The rule the ADR exists for.</summary>
    [Fact]
    public async Task An_address_with_an_account_is_not_linked_by_the_address()
    {
        var (_, owner, email) = await PasswordAccount();

        var back = await RoundTrip(Browser(), "google", Person(ExternalProvider.Google, email));

        back.Headers.Location!.ToString().Should().Be($"{Web}/login?external=address_taken");
        back.Headers.Contains("Set-Cookie").Should().BeTrue();
        back.Headers.GetValues("Set-Cookie").Should().NotContain(value => value.StartsWith("access=", StringComparison.Ordinal));
        (await Database(context => context.ExternalLogins.CountAsync(login => login.UserId == owner))).Should().Be(0);
    }

    /// <summary>Microsoft's address is a tenant's statement, not a proof (measured 18.10.2026).</summary>
    [Fact]
    public async Task A_microsoft_address_is_confirmed_by_our_own_mail()
    {
        var email = Unique("contoso.example");
        var sentBefore = _mail.Sent.Count;

        var back = await RoundTrip(Browser(), "microsoft", Person(ExternalProvider.Microsoft, email, verified: true));

        back.Headers.Location!.ToString().Should().Be($"{Web}/login?external=check_mail");
        var row = await Database(context => context.Users.SingleAsync(user => user.Email == email));
        row.Status.Should().Be(AccountStatus.Pending, "Microsoft vouches for no address, whatever the token says");
        _mail.Sent.Skip(sentBefore).Should().ContainSingle(mail => mail.To == email && mail.Text.Contains("token="));
    }

    /// <summary>And for a known address the answer is the same — it reveals nobody.</summary>
    [Fact]
    public async Task A_microsoft_sign_in_on_a_known_address_answers_like_an_unknown_one()
    {
        var (_, owner, email) = await PasswordAccount();
        var sentBefore = _mail.Sent.Count;

        var back = await RoundTrip(Browser(), "microsoft", Person(ExternalProvider.Microsoft, email));

        back.Headers.Location!.ToString().Should().Be($"{Web}/login?external=check_mail");
        (await Database(context => context.ExternalLogins.CountAsync(login => login.UserId == owner))).Should().Be(0);
        _mail.Sent.Skip(sentBefore).Should().ContainSingle(mail => mail.To == email && !mail.Text.Contains("token="));
    }

    [Fact]
    public async Task A_foreign_state_or_another_browser_signs_nobody_in()
    {
        var email = Unique();

        var forged = await RoundTrip(Browser(), "google", Person(ExternalProvider.Google, email), state: "forged");
        forged.Headers.Location!.ToString().Should().Be($"{Web}/login?external=failed");

        // A callback in a browser that never started the flow.
        _provider.Next = Person(ExternalProvider.Google, email);
        var stranger = await Browser().GetAsync("/auth/external/google/callback?code=c&state=whatever");
        stranger.Headers.Location!.ToString().Should().Be($"{Web}/login?external=failed");

        (await Database(context => context.Users.CountAsync(user => user.Email == email))).Should().Be(0);
    }

    [Fact]
    public async Task Apple_comes_back_as_a_form_post_and_names_the_person_only_there()
    {
        var browser = Browser();
        var email = Unique("privaterelay.appleid.com");
        var identity = new ExternalIdentity(ExternalProvider.Apple, Guid.NewGuid().ToString("N"), email, true, null, null, null);

        var back = await RoundTrip(browser, "apple", identity);

        back.Headers.Location!.ToString().Should().Be($"{Web}/overview");
        var row = await Database(context => context.Users.SingleAsync(user => user.Email == email));
        (row.GivenName, row.FamilyName).Should().Be(("Anna", "Apfel"));
    }

    [Fact]
    public async Task A_signed_in_person_links_a_provider_and_nobody_else_can_take_it()
    {
        var (browser, subject, _) = await PasswordAccount();
        var google = Person(ExternalProvider.Google, Unique());

        var linked = await RoundTrip(browser, "google", google, intent: "link");

        linked.Headers.Location!.ToString().Should().Be($"{Web}/settings?external=linked");
        var links = JsonDocument.Parse(await (await browser.GetAsync("/auth/external/links")).Content.ReadAsStringAsync()).RootElement;
        links.GetProperty("has_password").GetBoolean().Should().BeTrue();
        links.GetProperty("linked")[0].GetProperty("provider").GetString().Should().Be("google");

        // The same Google account, another person's account: refused.
        var (other, _, _) = await PasswordAccount();
        var taken = await RoundTrip(other, "google", google, intent: "link");
        taken.Headers.Location!.ToString().Should().Be($"{Web}/settings?external=link_taken");

        // And signing in with it now reaches the first account.
        var signIn = await RoundTrip(Browser(), "google", google);
        signIn.Headers.Location!.ToString().Should().Be($"{Web}/overview");
        (await Database(context => context.ExternalLogins.SingleAsync(login => login.ProviderSubject == google.Subject)))
            .UserId.Should().Be(subject);
    }

    [Fact]
    public async Task Linking_without_being_signed_in_is_no_sign_in()
    {
        var start = await Browser().GetAsync("/auth/external/google/start?intent=link");

        start.Headers.Location!.ToString().Should().Be($"{Web}/settings?external=failed");
    }

    [Fact]
    public async Task The_last_way_in_stays_and_a_second_one_may_go()
    {
        var browser = Browser();
        await RoundTrip(browser, "google", Person(ExternalProvider.Google, Unique()));

        var last = await browser.DeleteAsync("/auth/external/links/google");
        last.StatusCode.Should().Be(HttpStatusCode.Conflict, "without a password nothing would let her in again");

        var (withPassword, _, _) = await PasswordAccount();
        await RoundTrip(withPassword, "google", Person(ExternalProvider.Google, Unique()), intent: "link");
        (await withPassword.DeleteAsync("/auth/external/links/google")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await withPassword.DeleteAsync("/auth/external/links/google")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_account_without_a_password_takes_no_password()
    {
        var email = Unique();
        await RoundTrip(Browser(), "google", Person(ExternalProvider.Google, email));

        var attempt = await Browser().PostAsJsonAsync("/auth/login", new { email, password = "" + Password });

        attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_links_fall_with_the_account()
    {
        var email = Unique();
        await RoundTrip(Browser(), "google", Person(ExternalProvider.Google, email));
        var id = (await Database(context => context.Users.SingleAsync(user => user.Email == email))).Id;

        await Database(context => context.Users.Where(user => user.Id == id).ExecuteDeleteAsync());

        (await Database(context => context.ExternalLogins.CountAsync(login => login.UserId == id)))
            .Should().Be(0, "the cascade, not a line in the erasure somebody could forget (ADR-0027)");
    }

    /// <summary>A provider that answers what the test set, and remembers what it was handed.</summary>
    private sealed class ProviderProbe : IExternalProviders
    {
        public ExternalIdentity? Next { get; set; }

        public HashSet<ExternalProvider> Off { get; } = [];

        public string? LastVerifier { get; private set; }

        public string? LastNonce { get; private set; }

        public IReadOnlyList<ExternalProvider> Configured => [.. ExternalProviders.All.Where(provider => !Off.Contains(provider))];

        public Uri AuthorizationUrl(ExternalProvider provider, string state, string codeChallenge, string nonce) =>
            new($"https://provider.test/{ExternalProviders.Word(provider)}?state={Uri.EscapeDataString(state)}"
                + $"&challenge={Uri.EscapeDataString(codeChallenge)}&nonce={Uri.EscapeDataString(nonce)}");

        public Task<ExternalIdentity?> ExchangeAsync(
            ExternalProvider provider, string code, string codeVerifier, string nonce, CancellationToken cancellationToken = default)
        {
            LastVerifier = codeVerifier;
            LastNonce = nonce;
            return Task.FromResult(Next is { } next && next.Provider == provider ? next : null);
        }
    }
}
