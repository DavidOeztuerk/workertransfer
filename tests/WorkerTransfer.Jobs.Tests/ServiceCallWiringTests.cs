using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using WorkerTransfer.Jobs.Infrastructure.Sources;
using WorkerTransfer.Jobs.Infrastructure.Draft;
using WorkerTransfer.ServiceDefaults.Ai;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Tests.Shared;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>
/// The clients of the real <c>Program</c>: a door client carries the token of
/// its audience, every other carries none (ADR-0057).
/// </summary>
[Collection(PostgresCollection.Name)]
public class ServiceCallWiringTests(Postgres postgres) : IAsyncLifetime
{
    private readonly ServiceCallProbe _probe = new();
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(KeyRingSecret.Variable, "test-ring-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(_probe.Install);
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(HttpCompanyDomains.ClientName, ServiceAudiences.Identity)]
    [InlineData(HttpCompanyRoles.ClientName, ServiceAudiences.Identity)]
    [InlineData(HttpCompanyAiAccess.ClientName, ServiceAudiences.Identity)]
    public async Task A_door_client_carries_the_token_of_its_audience(string client, string audience) =>
        (await _probe.AudienceOf(_service.Services, client)).Should().Be(audience);

    [Theory]
    [InlineData(HttpCompanySite.ClientName)]
    [InlineData(HttpCompanyAiAccess.ModelClientName)]
    [InlineData(HttpSourceReader.ClientName)]
    [InlineData(nameof(HttpDrafter))]
    public async Task Every_other_client_carries_no_token(string client) =>
        (await _probe.AudienceOf(_service.Services, client)).Should().BeNull();
}
