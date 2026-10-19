using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Consent.Infrastructure.Persistence;

namespace WorkerTransfer.Consent.Tests;

/// <summary>A real Postgres, carrying this service's own schema.</summary>
/// <remarks>
/// The ledger's whole job is a reduction over rows, and it is done twice — once
/// in memory as the canonical rule, once in SQL for the read path. A fake
/// database could only ever agree with one of them.
/// </remarks>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("consent")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string for the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var quelle = ConsentDbContextFactory.DataSource(ConnectionString);
        await using var kontext = new ConsentDbContext(
            (DbContextOptions<ConsentDbContext>)ConsentDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<ConsentDbContext>(), quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>The token shape this service verifies, as identity-service issues it.</summary>
/// <remarks>
/// Minted here rather than mocked out: the whole point of the ledger being open
/// to any authenticated caller is that the authentication is real. A test host
/// with authentication stubbed away would prove the endpoints work for
/// everybody, which is not the claim.
/// </remarks>
public static class Tokenform
{
    /// <summary>Der private Testschlüssel (P-256, PKCS#8, base64).</summary>
    /// <remarks>
    /// Fest im Baum, weil er nur hier gilt: der Dienst unter Test bekommt den
    /// passenden öffentlichen Schlüssel. Gewürfelt machte er die Reise von der
    /// Maschine abhängig, auf der sie läuft.
    /// </remarks>
    public const string PrivatSchluessel = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>Der passende öffentliche Schlüssel (SPKI, base64).</summary>
    public const string OeffentlichSchluessel = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Benennt den Schlüssel, damit ein Wechsel zwei nebeneinander erlaubt.</summary>
    public const string Kennung = "test";

    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    private static readonly ECDsa Kurve = Lies();

    private static ECDsa Lies()
    {
        var kurve = ECDsa.Create();
        kurve.ImportPkcs8PrivateKey(Convert.FromBase64String(PrivatSchluessel), out _);
        return kurve;
    }

    /// <summary>An access token for one person, acting as themselves.</summary>
    public static string Fuer(Guid wer)
    {
        var schluessel = new ECDsaSecurityKey(Kurve) { KeyId = Kennung };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, wer.ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                schluessel, SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
