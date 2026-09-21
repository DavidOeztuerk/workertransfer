using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Roles;

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

        await using var source = CompaniesDbContextFactory.DataSource(ConnectionString);
        await using var context = new CompaniesDbContext(
            CompaniesDbContextFactory.Optionen(source).Options);

        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Ein eigener Kontext für Blicke in die Datenbank.</summary>
    public CompaniesDbContext Kontext() =>
        new(CompaniesDbContextFactory
            .Optionen(CompaniesDbContextFactory.DataSource(ConnectionString)).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
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

    public static string Person(Guid subject) => Build(subject, null);

    public static string Firma(Guid subject, Guid company) => Build(subject, company);

    private static string Build(Guid subject, Guid? company)
    {
        List<Claim> ansprueche = [new(JwtRegisteredClaimNames.Sub, subject.ToString())];

        if (company is { } tenant)
        {
            ansprueche.Add(new Claim("tenant", tenant.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience, claims: ansprueche,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(Kurve) { KeyId = Kennung },
                SecurityAlgorithms.EcdsaSha256));

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
public sealed class Rollenprobe : ICompanyRoles
{
    /// <summary>Was sie antwortet.</summary>
    public CompanyRole Antwort { get; set; } = CompanyRole.Admin;

    /// <summary>Ob sie stattdessen schweigt — der Ausfall von identity-service.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Wonach zuletzt gefragt wurde.</summary>
    public (Guid Wer, Guid Firma)? Zuletzt { get; private set; }

    /// <inheritdoc />
    public Task<CompanyRole> RoleAsync(
        Guid subject, Guid company, CancellationToken cancellationToken = default)
    {
        Zuletzt = (subject, company);

        return Schweigt
            ? throw new RoleLookupSilent("Die Probe schweigt.")
            : Task.FromResult(Antwort);
    }
}
