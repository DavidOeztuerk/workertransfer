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
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>The erasure — here without any exception.</summary>
[Collection(PostgresCollection.Name)]
public class ErasureTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeGitHub _github = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:github", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IGitHub>(_ => _github));
                services.Replace(ServiceDescriptor.Scoped<IConsentGate, ProbeLedger>());
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The link falls — and the personal datum with it.
    /// </summary>
    /// <remarks>
    /// The login is public; anybody can look it up on github.com. The personal
    /// datum is not the name but the link "this platform person is that GitHub
    /// name".
    /// </remarks>
    [Fact]
    public async Task The_erasure_takes_the_link_with_it()
    {
        var anna = Guid.CreateVersion7();
        await Connect(anna, "anna-dev");

        var response = await Erase(anna);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        receipt.GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = postgres.Context();
        (await context.Connections.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>Erasing one human being touches nobody else.</summary>
    [Fact]
    public async Task The_erasure_strikes_only_this_human_being()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();
        await Connect(anna, "anna-dev");
        await Connect(bea, "bea-dev");

        await Erase(anna);

        await using var context = postgres.Context();
        (await context.Connections.AnyAsync(row => row.Id == bea)).Should().BeTrue();
    }

    /// <summary>Without the secret nothing happens.</summary>
    [Fact]
    public async Task Without_the_right_secret_nothing_is_erased()
    {
        var anna = Guid.CreateVersion7();
        await Connect(anna, "anna-dev");

        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "guessed");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Context();
        (await context.Connections.AnyAsync(row => row.Id == anna)).Should().BeTrue();
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

    private Task<HttpResponseMessage> Connect(Guid subject, string login)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));

        return browser.PostAsJsonAsync("/github/me", new { login });
    }
}
