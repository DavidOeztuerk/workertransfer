using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Tests;

/// <summary>The erasure — here without any exception.</summary>
[Collection(PostgresCollection.Name)]
public class ErasureTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";
    private const string NotifySecret = "notify-secret";

    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:notification", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Identity:Adresse", "http://identity.test");
            host.UseSetting("Identity:Geheimnis", NotifySecret);
            host.UseSetting("Erasure:Geheimnis", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IMailer, ProbeMailer>()));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inbox and settings fall — no switch, no exception.
    /// </summary>
    /// <remarks>
    /// There is no row here in which somebody merely <em>acted</em>; every one
    /// is <em>about</em> the person.
    /// </remarks>
    [Fact]
    public async Task The_erasure_takes_inbox_and_settings_with_it()
    {
        var anna = Guid.CreateVersion7();
        await Notify(anna);
        await Preferences(anna);

        await using (var before = postgres.Context())
        {
            (await before.InboxEntries.AnyAsync(row => row.UserId == anna)).Should().BeTrue();
            (await before.Preferences.AnyAsync(row => row.Id == anna)).Should().BeTrue();
        }

        var response = await Erase(anna);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        receipt.GetProperty("retained").GetInt32().Should().Be(0);

        await using var after = postgres.Context();
        (await after.InboxEntries.AnyAsync(row => row.UserId == anna)).Should().BeFalse();
        (await after.Preferences.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>Erasing one human being touches nobody else.</summary>
    [Fact]
    public async Task The_erasure_strikes_only_this_human_being()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();
        await Notify(anna);
        await Notify(bea);

        await Erase(anna);

        await using var context = postgres.Context();
        (await context.InboxEntries.AnyAsync(row => row.UserId == bea)).Should().BeTrue();
    }

    /// <summary>Without the secret nothing happens.</summary>
    [Fact]
    public async Task Without_the_right_secret_nothing_is_erased()
    {
        var anna = Guid.CreateVersion7();
        await Notify(anna);

        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "guessed");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Context();
        (await context.InboxEntries.AnyAsync(row => row.UserId == anna)).Should().BeTrue();
    }

    /// <summary>
    /// The erasure secret is a different one from the notification's.
    /// </summary>
    /// <remarks>
    /// "May trigger a mail" and "may erase everything about a human being" must
    /// not be the same piece of paper (ADR-0027 §4.4).
    /// </remarks>
    [Fact]
    public async Task The_notify_secret_does_not_open_the_erasure()
    {
        var anna = Guid.CreateVersion7();
        await Notify(anna);

        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", NotifySecret);

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Context();
        (await context.InboxEntries.AnyAsync(row => row.UserId == anna)).Should().BeTrue();
    }

    /// <summary>A second call is not a failure.</summary>
    [Fact]
    public async Task Erasing_a_second_time_is_2xx_again()
    {
        var anna = Guid.CreateVersion7();

        (await Erase(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Erase(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> Erase(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);
        return browser.PostAsJsonAsync("/erasure", new { user_id = subject });
    }

    /// <summary>Creates an inbox entry — and insists that it works.</summary>
    /// <remarks>
    /// The answer is CHECKED. Before, it was thrown away, and a failed fill
    /// reported itself three lines later as "no entry" — a cause that looks
    /// like an effect.
    /// </remarks>
    private async Task<HttpResponseMessage> Notify(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", NotifySecret);

        var response = await browser.PostAsJsonAsync(
            "/internal/notifications", new { user_id = subject, kind = "market_request" });

        response.IsSuccessStatusCode.Should().BeTrue(
            "the fill must succeed, otherwise the test checks something else — "
            + $"got {(int)response.StatusCode}: "
            + await response.Content.ReadAsStringAsync());

        return response;
    }

    private Task<HttpResponseMessage> Preferences(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));

        return browser.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = false,
            market_request = true,
            application_update = true,
            transfer_update = true
        });
    }
}
