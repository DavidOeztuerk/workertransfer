using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Rollen;

namespace WorkerTransfer.Companies.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("companies")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var quelle = CompaniesDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new CompaniesDbContext(
            CompaniesDbContextFactory.Optionen(quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Ein eigener Kontext für Blicke in die Datenbank.</summary>
    public CompaniesDbContext Kontext() =>
        new(CompaniesDbContextFactory
            .Optionen(CompaniesDbContextFactory.Datenquelle(ConnectionString)).Options);
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

/// <summary>Eine Rollenauskunft, die nicht fragt, sondern antwortet.</summary>
/// <remarks>
/// <para>Sie ersetzt den Draht zu identity-service. Ohne sie müsste jede Reihe,
/// die eine Firmenhandlung anfasst, einen zweiten Dienst mitstarten.</para>
///
/// <para><strong>Sie MERKT SICH, wonach gefragt wurde.</strong> Eine Attrappe,
/// die ihre Eingaben wegwirft, hat in diesem Baum schon dreimal einen echten
/// Fehler verdeckt: sie antwortet richtig, auch wenn der Aufrufer die falsche
/// Firma übergibt. Hier ist genau das die Frage — der Mandant muss aus dem
/// TOKEN kommen.</para>
/// </remarks>
public sealed class Rollenprobe : IFirmenrollen
{
    /// <summary>Was sie antwortet.</summary>
    public Firmenrolle Antwort { get; set; } = Firmenrolle.Admin;

    /// <summary>Ob sie stattdessen schweigt — der Ausfall von identity-service.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Wonach zuletzt gefragt wurde.</summary>
    public (Guid Wer, Guid Firma)? Zuletzt { get; private set; }

    /// <inheritdoc />
    public Task<Firmenrolle> RolleAsync(
        Guid wer, Guid firma, CancellationToken cancellationToken = default)
    {
        Zuletzt = (wer, firma);

        return Schweigt
            ? throw new RolleSchweigt("Die Probe schweigt.")
            : Task.FromResult(Antwort);
    }
}
