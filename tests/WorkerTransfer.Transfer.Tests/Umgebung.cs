using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("transfer")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var quelle = TransferDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new TransferDbContext(
            TransferDbContextFactory.Optionen(quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Ein eigener Kontext für Blicke in die Datenbank.</summary>
    public TransferDbContext Kontext() =>
        new(TransferDbContextFactory
            .Optionen(TransferDbContextFactory.Datenquelle(ConnectionString)).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>Die Tokenform, die dieser Dienst prüft.</summary>
public static class Tokenform
{
    public const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    public static string Person(Guid wer) => Baue(wer, null);

    public static string Firma(Guid wer, Guid firma) => Baue(wer, firma);

    private static string Baue(Guid wer, Guid? firma)
    {
        List<Claim> ansprueche = [new(JwtRegisteredClaimNames.Sub, wer.ToString())];

        if (firma is { } mandant)
        {
            ansprueche.Add(new Claim("tenant", mandant.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience, claims: ansprueche,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Geheimnis)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
