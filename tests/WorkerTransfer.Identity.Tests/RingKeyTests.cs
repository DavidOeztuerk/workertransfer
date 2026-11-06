using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>The one service with both keys keeps them apart.</summary>
/// <remarks>
/// Here and not at another service, because only here both stand in one container
/// (ADR-0058). With two registrations of one type the last would win, and the ring
/// would lie under the master key again without any start saying so.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RingKeyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public async Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting(KeyRingSecret.Variable, "test-ring-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Mail:WebUrl", "http://localhost:5173");
            host.UseSetting("environment", "Development");
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
    public async Task The_ring_does_not_open_with_the_master_key()
    {
        // Protecting forces a key to come into being and be stored.
        _service.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("ring-key").Protect("something");

        using var scope = _service.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.SqlQueryRaw<string>("SELECT content AS \"Value\" FROM dataprotection_keys")
            .ToListAsync();

        rows.Should().NotBeEmpty("otherwise this test checks an empty table");

        var masterKey = _service.Services.GetRequiredService<SecretStore>();
        var ring = _service.Services.GetRequiredService<KeyRingSecret>();

        foreach (var row in rows)
        {
            var sealedKey = XElement.Parse(row).Descendants("wtProtectedKey").Single().Value;

            masterKey.Decrypt(sealedKey).Should().BeNull("whoever holds the master key holds no ring");
            ring.Decrypt(sealedKey).Should().NotBeNull("otherwise nothing here lies under the ring's key");
        }
    }
}
