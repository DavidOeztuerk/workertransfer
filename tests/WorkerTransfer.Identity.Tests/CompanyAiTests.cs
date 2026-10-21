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
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// A company's own AI key: set by an admin, never handed back, handed to a
/// service only through the internal door (ADR-0051, Bauliste 6).
/// </summary>
[Collection(PostgresCollection.Name)]
public class CompanyAiTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";
    private const string NotifySecret = "notify-secret";
    private const string Key = "sk-ant-company-secret-7f3a";

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
            host.UseSetting("Notify:Secret", NotifySecret);
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

    [Fact]
    public async Task An_admin_stores_a_key_and_the_view_never_carries_it()
    {
        var (boss, company) = await Founder();

        (await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await boss.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = Key }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var view = await boss.GetAsync($"/companies/{company}/ai");
        var text = await view.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;

        json.GetProperty("ai_provider").GetString().Should().Be("anthropic");
        json.GetProperty("ai_base_url").GetString().Should().Be("https://api.anthropic.com/v1/messages");
        json.GetProperty("ai_model").GetString().Should().NotBeEmpty("an empty model takes the provider's default");
        json.GetProperty("ai_key_present").GetBoolean().Should().BeTrue();
        json.GetProperty("ai_key_tail").GetString().Should().Be("7f3a");
        text.Should().NotContain("company-secret");
    }

    [Fact]
    public async Task The_key_lies_encrypted_in_the_row()
    {
        var (boss, company) = await Founder();
        await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" });
        await boss.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = Key });

        using var scope = _service.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .CompanyAiSettings.SingleAsync(entry => entry.TenantId == Guid.Parse(company));

        row.AiKeyEncrypted.Should().NotBeEmpty().And.NotContain("company-secret");
    }

    [Fact]
    public async Task The_internal_door_hands_the_key_to_a_service_and_to_nobody_else()
    {
        var (boss, company) = await Founder();
        await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic", ai_model = "claude-opus-5-5" });
        await boss.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = Key });

        var service = _service.CreateClient();
        service.DefaultRequestHeaders.Add("X-Notify-Secret", NotifySecret);
        var access = JsonDocument.Parse(
            await (await service.GetAsync($"/internal/companies/{company}/ai")).Content.ReadAsStringAsync()).RootElement;

        access.GetProperty("provider").GetString().Should().Be("anthropic");
        access.GetProperty("base_url").GetString().Should().Be("https://api.anthropic.com/v1/messages");
        access.GetProperty("model").GetString().Should().Be("claude-opus-5-5");
        access.GetProperty("key").GetString().Should().Be(Key);

        (await _service.CreateClient().GetAsync($"/internal/companies/{company}/ai"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_key_the_door_says_none_whatever_provider_is_chosen()
    {
        var (boss, company) = await Founder();
        await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "openai", ai_model = "gpt-5" });

        var service = _service.CreateClient();
        service.DefaultRequestHeaders.Add("X-Notify-Secret", NotifySecret);
        var access = JsonDocument.Parse(
            await (await service.GetAsync($"/internal/companies/{company}/ai")).Content.ReadAsStringAsync()).RootElement;

        access.GetProperty("provider").GetString().Should().Be("none");
        access.GetProperty("key").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task Choosing_the_platform_removes_the_key()
    {
        var (boss, company) = await Founder();
        await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" });
        await boss.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = Key });

        var view = JsonDocument.Parse(await (await boss.PutAsJsonAsync(
            $"/companies/{company}/ai", new { ai_provider = "none" })).Content.ReadAsStringAsync()).RootElement;

        view.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();

        using var scope = _service.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .CompanyAiSettings.SingleAsync(entry => entry.TenantId == Guid.Parse(company));
        row.AiKeyEncrypted.Should().BeEmpty("a secret with no purpose is the worst kind to keep");
    }

    /// <summary>The AI directory of the evidence counts the company — as a company, never by name.</summary>
    [Fact]
    public async Task The_evidence_counts_a_company_key_without_naming_the_company()
    {
        var (boss, company) = await Founder();
        await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" });
        await boss.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = Key });

        var rows = new List<ServiceDefaults.Checks.ProviderRow>();
        foreach (var source in _service.Services.GetServices<ServiceDefaults.Checks.IProviderSource>())
        {
            rows.AddRange(await source.ReadAsync());
        }

        rows.Should().Contain(row => row.Origin == ServiceDefaults.Checks.ProviderOrigin.Company
                                     && row.Host == "api.anthropic.com" && row.People >= 1);
        rows.Select(row => row.Label + row.Host).Should().NotContain(text => text.Contains(company));
    }

    [Fact]
    public async Task An_unknown_provider_is_refused_not_read_as_none()
    {
        var (boss, company) = await Founder();

        var answer = await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "gemini" });

        answer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_member_may_neither_read_nor_change_it()
    {
        var (boss, company) = await Founder();
        var member = await Member(boss, company);

        (await member.GetAsync($"/companies/{company}/ai"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = "sk-member" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stranger_is_refused_too()
    {
        var (_, company) = await Founder();
        var stranger = await Person(Address("stranger", NewDomain()));

        (await stranger.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = "sk-stranger" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A company whose last admin erased their account goes dormant — and
    /// nobody is left who may use or change its key. A tenant is never deleted,
    /// so no cascade would take the row.
    /// </summary>
    [Fact]
    public async Task A_dormant_company_keeps_no_key()
    {
        var (boss, company) = await Founder();
        await boss.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" });
        await boss.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = Key });
        var subject = JsonDocument.Parse(await (await boss.GetAsync("/me")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("user_id").GetString()!;

        (await boss.PostAsync("/account/erasure", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Application.Erasure.IErasureLedger>()
            .CompleteAsync(new Noelia.Core.Identity.SubjectId(Guid.Parse(subject)), DateTimeOffset.UtcNow);

        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await context.CompanyAiSettings.AnyAsync(entry => entry.TenantId == Guid.Parse(company)))
            .Should().BeFalse();
    }

    private HttpClient Browser() =>
        _service.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private string LastLink(string subjectPart)
    {
        var text = _sender.Sent.Last(mail =>
            mail.Subject.Contains(subjectPart, StringComparison.Ordinal)).Text;

        return Regex.Match(text, @"token=(?<token>[A-Za-z0-9_-]+)",
            RegexOptions.None, TimeSpan.FromSeconds(5)).Groups["token"].Value;
    }

    private async Task<HttpClient> Person(string email, string? company = null)
    {
        var browser = Browser();

        (await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Password,
            display_name = email.Split('@')[0],
            company_name = company
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await browser.PostAsJsonAsync("/auth/verify-email", new { token = LastLink("bestätige") }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await browser.PostAsJsonAsync("/auth/login", new { email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return browser;
    }

    private async Task<(HttpClient Browser, string Company)> Founder()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Schweißtechnik GmbH");
        var mine = JsonDocument.Parse(await (await boss.GetAsync("/me/companies")).Content.ReadAsStringAsync());
        return (boss, mine.RootElement[0].GetProperty("id").GetString()!);
    }

    private async Task<HttpClient> Member(HttpClient boss, string company)
    {
        var colleague = Address("colleague", "other.example");
        (await boss.PostAsJsonAsync($"/companies/{company}/invitations", new { email = colleague, role = "member" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var browser = await Person(colleague);
        (await browser.PostAsJsonAsync("/invitations/accept", new { token = LastLink("eingeladen") }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return browser;
    }

    private static string Address(string name, string domain) => $"{name}-{Guid.NewGuid():N}@{domain}";

    private static string NewDomain() => $"company-{Guid.NewGuid():N}.example";
}
