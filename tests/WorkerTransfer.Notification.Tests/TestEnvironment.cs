using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Infrastructure.Persistence;

namespace WorkerTransfer.Notification.Tests;

/// <summary>A real Postgres carrying this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("notification")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = NotificationDbContextFactory.DataSource(ConnectionString);
        await using var context = new NotificationDbContext(
            NotificationDbContextFactory.Options(source).Options);

        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A context of its own, for looking into the database.</summary>
    public NotificationDbContext Context() =>
        new(NotificationDbContextFactory
            .Options(NotificationDbContextFactory.DataSource(ConnectionString)).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>A mailer that writes down instead of sending.</summary>
/// <remarks>
/// It knows no address, and that is the touchstone: what the test sees is a
/// <c>SubjectId</c> — nothing more crosses the boundary in operation either.
/// </remarks>
public sealed class ProbeMailer : IMailer
{
    /// <summary>Who mail was asked for, in the order of the calls.</summary>
    public List<Guid> Requested { get; } = [];

    /// <inheritdoc />
    public Task SendAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        Requested.Add(subject.Value);
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test sets.</summary>
public sealed class ProbeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Lets time pass.</summary>
    public void Advance(TimeSpan span) => _now += span;
}

/// <summary>The token shape this service verifies.</summary>
public static class TokenShape
{
    /// <summary>The private test key (P-256, PKCS#8, base64).</summary>
    /// <remarks>
    /// Fixed in the tree because it applies only here: the service under test
    /// gets the matching public key. Rolled, it would make the journey depend
    /// on the machine it runs on.
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

    public static string Person(Guid subject)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subject.ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(Curve) { KeyId = KeyId },
                SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
