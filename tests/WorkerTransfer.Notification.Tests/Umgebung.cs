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

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
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

        await using var quelle = NotificationDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new NotificationDbContext(
            NotificationDbContextFactory.Optionen(quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Ein eigener Kontext für Blicke in die Datenbank.</summary>
    public NotificationDbContext Kontext() =>
        new(NotificationDbContextFactory
            .Optionen(NotificationDbContextFactory.Datenquelle(ConnectionString)).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>Ein Postbote, der aufschreibt statt zu schicken.</summary>
/// <remarks>
/// Er kennt keine Adresse, und das ist der Prüfstein: was der Test sieht, ist
/// eine <c>SubjectId</c> — mehr geht auch im Betrieb nicht über die Grenze.
/// </remarks>
public sealed class Probepostbote : IPostbote
{
    /// <summary>Für wen um Post gebeten wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<Guid> Gebeten { get; } = [];

    /// <inheritdoc />
    public Task SchickeAsync(SubjectId wer, CancellationToken cancellationToken = default)
    {
        Gebeten.Add(wer.Value);
        return Task.CompletedTask;
    }
}

/// <summary>Eine Uhr, die der Test stellt.</summary>
public sealed class Probeuhr(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _jetzt = start;

    public override DateTimeOffset GetUtcNow() => _jetzt;

    /// <summary>Lässt Zeit vergehen.</summary>
    public void Weiter(TimeSpan spanne) => _jetzt += spanne;
}

/// <summary>Die Tokenform, die dieser Dienst prüft.</summary>
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

    public static string Person(Guid wer)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, wer.ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(Kurve) { KeyId = Kennung },
                SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
