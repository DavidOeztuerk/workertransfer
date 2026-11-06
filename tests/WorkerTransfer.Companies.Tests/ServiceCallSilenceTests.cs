using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Infrastructure.Security.M2M;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Companies.Tests;

/// <summary>
/// No token is a silent role lookup, through the real chain (ADR-0057): the
/// promise is 503 and never 403, and uncaught it would have been a 500.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ServiceCallSilenceTests(Postgres postgres) : IAsyncLifetime
{
    private sealed class Refusing : IServiceTokenSource
    {
        public Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken = default) =>
            throw new ServiceTokenUnavailableException("identity-service refused the client");
    }

    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:companies", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(KeyRingSecret.Variable, "test-ring-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Identity:BaseUrl", "http://identity-service:8001");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.AddSingleton<IServiceTokenSource>(new Refusing()));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_refused_token_at_the_role_door_is_a_503()
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Company(Guid.CreateVersion7(), Guid.CreateVersion7()));

        var response = await browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = "Example GmbH",
            about = "We build railings.",
            website = "https://example.test",
            locations = new[] { "Berlin" },
            benefits = new[] { "Tools provided" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
