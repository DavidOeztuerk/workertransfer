using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using WorkerTransfer.Applications.Infrastructure.Companies;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;
using WorkerTransfer.Applications.Infrastructure.Notification;
using WorkerTransfer.Applications.Infrastructure.Consent;
using WorkerTransfer.Applications.Infrastructure.Jobs;
using WorkerTransfer.Applications.Infrastructure.Lookup;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Tests.Shared;

namespace WorkerTransfer.Applications.Tests;

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
            host.UseSetting("ConnectionStrings:applications", postgres.ConnectionString);
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
    [InlineData(HttpCompanyMemberLookup.ClientName, ServiceAudiences.Identity)]
    [InlineData(nameof(HttpNotifier), ServiceAudiences.Notification)]
    public async Task A_door_client_carries_the_token_of_its_audience(string client, string audience) =>
        (await _probe.AudienceOf(_service.Services, client)).Should().Be(audience);

    /// <summary>Two lookups share one registered client; a new name would carry no token.</summary>
    [Fact]
    public void Both_lookups_at_identity_use_the_door_client() =>
        HttpAiAccessLookup.ClientName.Should().Be(HttpCompanyMemberLookup.ClientName);

    [Theory]
    [InlineData(HttpCoverLetterWriter.ClientName)]
    [InlineData(HttpApplicantLookup.ClientName)]
    [InlineData(HttpConsentWriter.ClientName)]
    [InlineData(HttpJobLookup.ClientName)]
    public async Task Every_other_client_carries_no_token(string client) =>
        (await _probe.AudienceOf(_service.Services, client)).Should().BeNull();
}
