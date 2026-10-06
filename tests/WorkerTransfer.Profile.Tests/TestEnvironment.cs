using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Profile.Infrastructure.Persistence;

namespace WorkerTransfer.Profile.Tests;

/// <summary>A real Postgres with this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("profile")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string to the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = ProfileDbContextFactory.DataSource(ConnectionString);
        await using var context = new ProfileDbContext(
            (DbContextOptions<ProfileDbContext>)ProfileDbContextFactory.Configure(
                new DbContextOptionsBuilder<ProfileDbContext>(), source).Options);

        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>The token shape this service verifies.</summary>
public static class TokenShape
{
    /// <summary>The private test key (P-256, PKCS#8, base64).</summary>
    /// <remarks>
    /// Fixed in the tree because it applies only here: the service under test
    /// gets the matching public key. Rolled, it would make the journey depend on
    /// the machine it runs on.
    /// </remarks>
    public const string PrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>The matching public key (SPKI, base64).</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Names the key, so a change may hold two of them side by side.</summary>
    public const string KeyId = "test";

    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    private static readonly ECDsa Curve = Read();

    private static ECDsa Read()
    {
        var curve = ECDsa.Create();
        curve.ImportPkcs8PrivateKey(Convert.FromBase64String(PrivateKey), out _);
        return curve;
    }

    /// <summary>A token for a person acting for themselves.</summary>
    public static string Person(Guid subject) => Build(subject, company: null);

    /// <summary>A token for somebody acting for a company.</summary>
    public static string Company(Guid subject, Guid company) => Build(subject, company);

    private static string Build(Guid subject, Guid? company)
    {
        // Like a real token from identity-service: the revocation check refuses one
        // without jti and iat.
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, subject.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        ];

        if (company is { } tenant)
        {
            claims.Add(new Claim("tenant", tenant.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(Curve) { KeyId = KeyId },
                SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
