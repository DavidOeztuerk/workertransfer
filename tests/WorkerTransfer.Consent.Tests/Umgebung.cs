using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
    public const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    /// <summary>An access token for one person, acting as themselves.</summary>
    public static string Fuer(Guid wer)
    {
        var schluessel = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Geheimnis));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, wer.ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                schluessel, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
