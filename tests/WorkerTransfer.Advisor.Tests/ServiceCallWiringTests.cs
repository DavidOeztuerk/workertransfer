using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using WorkerTransfer.Advisor.Infrastructure.Lookup;
using WorkerTransfer.Advisor.Infrastructure.Notification;
using WorkerTransfer.Advisor.Infrastructure.Consent;
using WorkerTransfer.Advisor.Infrastructure.Handover;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Tests.Shared;

namespace WorkerTransfer.Advisor.Tests;

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
            host.UseSetting("ConnectionStrings:advisor", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
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
    [InlineData(HttpLookups.ClientName, ServiceAudiences.Identity)]
    [InlineData(HttpCompanyRoles.ClientName, ServiceAudiences.Identity)]
    [InlineData(nameof(HttpNotifier), ServiceAudiences.Notification)]
    public async Task A_door_client_carries_the_token_of_its_audience(string client, string audience) =>
        (await _probe.AudienceOf(_service.Services, client)).Should().Be(audience);

    [Theory]
    [InlineData(HttpConsentGate.ClientName)]
    [InlineData(HttpCaseHandover.ClientName)]
    public async Task Every_other_client_carries_no_token(string client) =>
        (await _probe.AudienceOf(_service.Services, client)).Should().BeNull();
}
