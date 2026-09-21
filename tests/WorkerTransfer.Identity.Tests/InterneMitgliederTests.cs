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
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// <c>GET /internal/companies/{tenantId}/members</c> — the wire over which
/// applications-service resolves the recipients of an application mail.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InternalMembersTests(Postgres postgres) : IAsyncLifetime
{
    private const string NotifySecret = "melde-geheimnis";
    private const string Password = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly MailRecord _post = new();

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("Notify:Geheimnis", NotifySecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_post)));
        });

        using var scope = _dienst.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<(Guid Company, Guid Subject)> CompanyWithMember()
    {
        var domain = $"firma-{Guid.NewGuid():N}.example";
        var address = $"anna-{Guid.NewGuid():N}@{domain}";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address,
            password = Password,
            display_name = "Anna",
            company_name = "Beispiel GmbH"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = address, password = Password });

        var ich = await Json(await browser.GetAsync("/me"));
        var mine = await Json(await browser.GetAsync("/me/companies"));

        return (Guid.Parse(mine[0].GetProperty("id").GetString()!),
            Guid.Parse(ich.GetProperty("user_id").GetString()!));
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

        var response = await WithSecret("geraten")
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
