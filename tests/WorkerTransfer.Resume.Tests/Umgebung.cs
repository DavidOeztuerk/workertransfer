using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Resume.Infrastructure.Persistence;

namespace WorkerTransfer.Resume.Tests;

/// <summary>A real Postgres, carrying this service's own schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("resume")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string for the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var quelle = ResumeDbContextFactory.DataSource(ConnectionString);
        await using var kontext = new ResumeDbContext(
            (DbContextOptions<ResumeDbContext>)ResumeDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<ResumeDbContext>(), quelle).Options);

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
public static class Tokenform
{
    public const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    /// <summary>A token for a person acting as themselves.</summary>
    public static string Person(Guid wer) => Baue(wer, firma: null);

    /// <summary>A token for somebody acting for a company.</summary>
    /// <remarks>
    /// The claim name is the one identity-service writes. A résumé is released
    /// to a named company, so half of this service's rules read it.
    /// </remarks>
    public static string Firma(Guid wer, Guid firma) => Baue(wer, firma);

    private static string Baue(Guid wer, Guid? firma)
    {
        List<Claim> ansprueche = [new(JwtRegisteredClaimNames.Sub, wer.ToString())];

        if (firma is { } mandant)
        {
            ansprueche.Add(new Claim("tenant", mandant.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: ansprueche,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Geheimnis)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
