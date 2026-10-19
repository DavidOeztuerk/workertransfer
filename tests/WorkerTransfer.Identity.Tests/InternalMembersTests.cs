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
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// <c>GET /internal/companies/{tenantId}/members</c> — the wire over which
/// applications-service resolves the recipients of an application mail.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InternalMembersTests(Postgres postgres) : IAsyncLifetime
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
            host.UseSetting("Mail:WebUrl", "http://localhost:5173");
            host.UseSetting("Notify:Secret", NotifySecret);
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

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<(Guid Company, Guid Subject)> CompanyWithMember()
    {
        var domain = $"company-{Guid.NewGuid():N}.example";
        var address = $"anna-{Guid.NewGuid():N}@{domain}";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address,
            password = Password,
            display_name = "Anna",
            company_name = "Example GmbH"
        });

        var token = Regex.Match(
            _mail.Sent[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = address, password = Password });

        var me = await Json(await browser.GetAsync("/me"));
        var mine = await Json(await browser.GetAsync("/me/companies"));

        return (Guid.Parse(mine[0].GetProperty("id").GetString()!),
            Guid.Parse(me.GetProperty("user_id").GetString()!));
    }

    private HttpClient WithSecret(string secret = NotifySecret)
    {
        var browser = Browser();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", secret);
        return browser;
    }

    /// <summary>Without the secret the endpoint is invisible.</summary>
    [Fact]
    public async Task Without_the_secret_it_is_404()
    {
        var (company, _) = await CompanyWithMember();

        var response = await Browser().GetAsync($"/internal/companies/{company}/members");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A guessed secret does not lift the door.</summary>
    [Fact]
    public async Task A_guessed_secret_is_404()
    {
        var (company, _) = await CompanyWithMember();

        var response = await WithSecret("guessed")
            .GetAsync($"/internal/companies/{company}/members");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// With the secret the id comes — and it travels as <c>subject_id</c>, not
    /// as <c>subjectId</c>.
    /// </summary>
    [Fact]
    public async Task With_the_secret_the_members_come_back_in_snake_case()
    {
        var (company, subject) = await CompanyWithMember();

        var response = await WithSecret().GetAsync($"/internal/companies/{company}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("subject_id");
        body.Should().NotContain("subjectId");
        body.Should().NotContain("display_name");

        var read = await Json(response);
        var members = read.GetProperty("mitglieder");
        members.GetArrayLength().Should().Be(1);
        members[0].GetProperty("subject_id").GetGuid().Should().Be(subject);
    }

    /// <summary>
    /// An unknown company is an empty list, not a 404: otherwise the endpoint
    /// would be an oracle about whether this company exists.
    /// </summary>
    [Fact]
    public async Task An_unknown_company_is_an_empty_list()
    {
        var response = await WithSecret()
            .GetAsync($"/internal/companies/{Guid.CreateVersion7()}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("mitglieder").GetArrayLength().Should().Be(0);
    }
}
