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

/// <summary>The settings — and the secret that never comes back.</summary>
/// <remarks>
/// This suite's most important promise stands in
/// <c>The_key_never_comes_back</c>: the interface learns THAT one is stored,
/// and its last four characters. Anything else would be a secret one can
/// retrieve — and therefore siphon off.
/// <para>
/// And one that is easy to overlook: after an erasure no row with an encrypted
/// key may stay standing. `account_settings` has `subject_id` as its PRIMARY
/// KEY and points at nothing, so no CASCADE clears it away.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class SettingsTests(Postgres postgres) : IAsyncLifetime
{
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
            host.UseSetting("Notify:Geheimnis", "melde-geheimnis");
            // The master key. Without it the service throws while resolving —
            // and that is exactly what it should do, see `SecretStore`.
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
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

    private async Task<(HttpClient Browser, Guid Subject)> SignedIn()
    {
        var address = $"anna-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address, password = Password, display_name = "Anna"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = address, password = Password });

        var session = JsonDocument.Parse(
            await (await browser.GetAsync("/auth/session")).Content.ReadAsStringAsync());

        return (browser, session.RootElement.GetProperty("user").GetProperty("user_id").GetGuid());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Whoever never set anything gets the most reticent setting.</summary>
    /// <remarks>
    /// No provider, no lapse, no log. Defaulting to an outside provider would be
    /// a consent nobody gave.
    /// </remarks>
    [Fact]
    public async Task Without_a_row_the_most_reticent_setting_holds()
    {
        var (browser, _) = await SignedIn();

        var state = await Json(await browser.GetAsync("/account/settings"));

        state.GetProperty("ai_provider").GetString().Should().Be("none");
        state.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();
        state.GetProperty("ai_audit_log").GetBoolean().Should().BeFalse();
        state.GetProperty("delete_after_months").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>THE most important test: the secret travels one way only.</summary>
    [Fact]
    public async Task The_key_never_comes_back()
    {
        var (browser, _) = await SignedIn();
        const string key = "sk-ant-api03-GEHEIM-abcd1234WXYZ";

        (await browser.PutAsJsonAsync("/account/ai-key", new { key = key }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await browser.GetAsync("/account/settings");
        var raw = await response.Content.ReadAsStringAsync();
        var state = JsonDocument.Parse(raw).RootElement;

        state.GetProperty("ai_key_present").GetBoolean().Should().BeTrue();
        state.GetProperty("ai_key_tail").GetString().Should().Be("WXYZ");

        // The whole body, not only the one field: the key must appear nowhere
        // in it, not even as part of another.
        raw.Should().NotContain(key);
        raw.Should().NotContain("GEHEIM");
    }

    /// <summary>In the database it lies encrypted, not in the clear.</summary>
    [Fact]
    public async Task In_the_row_it_lies_encrypted()
    {
        var (browser, subject) = await SignedIn();
        const string key = "sk-test-KLARTEXT-0000";

        await browser.PutAsJsonAsync("/account/ai-key", new { key = key });

        using var scope = _dienst.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await context.AccountSettings.FirstAsync(e => e.SubjectId == subject);

        row.AiKeyEncrypted.Should().NotBeEmpty();
        row.AiKeyEncrypted.Should().NotContain("KLARTEXT");
        row.AiKeyTail.Should().Be("0000");

        // And the round trip holds: what was stored comes back out.
        var store = scope.ServiceProvider.GetRequiredService<SecretStore>();
        store.Decrypt(row.AiKeyEncrypted).Should().Be(key);
    }

    /// <summary>An empty key REMOVES — it does not leave unchanged.</summary>
    /// <remarks>
    /// A field that does nothing when empty has no way back to „none" — and then
    /// a secret nobody needs any more would stay lying about.
    /// </remarks>
    [Fact]
    public async Task An_empty_key_removes_it()
    {
        var (browser, _) = await SignedIn();

        await browser.PutAsJsonAsync("/account/ai-key", new { key = "sk-test-1234" });
        await browser.PutAsJsonAsync("/account/ai-key", new { key = "" });

        var state = await Json(await browser.GetAsync("/account/settings"));
        state.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();
        state.GetProperty("ai_key_tail").GetString().Should().BeEmpty();
    }

    /// <summary>„No provider" clears the key away with it.</summary>
    /// <remarks>
    /// A secret with no purpose is the worst thing there is to keep.
    /// </remarks>
    [Fact]
    public async Task No_provider_clears_the_key_away()
    {
        var (browser, _) = await SignedIn();

        await browser.PutAsJsonAsync("/account/ai-key", new { key = "sk-test-1234" });
        await browser.PutAsJsonAsync("/account/settings", new
        {
            delete_after_months = (int?)null,
            ai_provider = "none",
            ai_base_url = "",
            ai_model = "",
            ai_audit_log = false
        });

        var state = await Json(await browser.GetAsync("/account/settings"));
        state.GetProperty("ai_key_present").GetBoolean().Should().BeFalse();
    }

    /// <summary>A lapse that is too short is refused, not silently rounded.</summary>
    /// <remarks>
    /// An account that disappears after four weeks is one somebody loses while
    /// on holiday.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    public async Task A_nonsensical_lapse_is_refused(int months)
    {
        var (browser, _) = await SignedIn();

        var response = await browser.PutAsJsonAsync("/account/settings", new
        {
            delete_after_months = months,
            ai_provider = "none",
            ai_base_url = "",
            ai_model = "",
            ai_audit_log = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Without a sign-in nothing works — neither reading nor writing.</summary>
    [Fact]
    public async Task Without_a_sign_in_nothing_works()
    {
        (await Browser().GetAsync("/account/settings"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Browser().PutAsJsonAsync("/account/ai-key", new { key = "sk-fremd" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The erasure takes the settings with it — secret included.</summary>
    /// <remarks>
    /// `account_settings` has `subject_id` as its PRIMARY KEY and points at
    /// nothing. No CASCADE clears it away; without its own line in the erasure
    /// ledger an encrypted key would stay standing for an account that no longer
    /// exists.
    /// </remarks>
    [Fact]
    public async Task The_erasure_takes_the_settings_with_it()
    {
        var (browser, subject) = await SignedIn();
        await browser.PutAsJsonAsync("/account/ai-key", new { key = "sk-test-bleibt-nicht" });

        using (var before = _dienst.Services.CreateScope())
        {
            var context = before.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.AccountSettings.AnyAsync(e => e.SubjectId == subject))
                .Should().BeTrue("ohne Zeile prüft dieser Test nichts");
        }

        (await browser.PostAsync("/account/erasure", null))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The cascade runs asynchronously; what counts here is the step that
        // clears this service's rows. It is driven over the ledger directly.
        using var scope = _dienst.Services.CreateScope();
        var erasureLedger = scope.ServiceProvider
            .GetRequiredService<Application.Erasure.IErasureLedger>();
        await erasureLedger.CompleteAsync(new Noelia.Core.Identity.SubjectId(subject), DateTimeOffset.UtcNow);

        var after = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await after.AccountSettings.AnyAsync(e => e.SubjectId == subject)).Should().BeFalse();
    }

    /// <summary>
    /// The internal wire carries the key — without the secret it is invisible.
    /// </summary>
    [Fact]
    public async Task Without_the_secret_the_internal_access_does_not_exist()
    {
        var (_, subject) = await SignedIn();

        var response = await _dienst.CreateClient()
            .GetAsync($"/internal/account/{subject}/ai");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ollama needs no key, and the internal wire says so.</summary>
    [Fact]
    public async Task A_server_of_one_s_own_travels_without_a_key()
    {
        var (browser, subject) = await SignedIn();

        (await browser.PutAsJsonAsync("/account/settings", new
        {
            delete_after_months = (int?)null,
            ai_provider = "openai_compatible",
            ai_base_url = "http://localhost:11434/v1/chat/completions",
            ai_model = "qwen2.5-coder:7b",
            ai_audit_log = false
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var intern = _dienst.CreateClient();
        intern.DefaultRequestHeaders.Add("X-Notify-Secret", "melde-geheimnis");

        var access = await Json(await intern.GetAsync($"/internal/account/{subject}/ai"));

        access.GetProperty("provider").GetString().Should().Be("openai_compatible");
        access.GetProperty("base_url").GetString()
            .Should().Be("http://localhost:11434/v1/chat/completions");
        access.GetProperty("model").GetString().Should().Be("qwen2.5-coder:7b");
        access.GetProperty("key").GetString().Should().BeEmpty();
    }
}
