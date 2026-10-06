using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Domain.Cases;
using WorkerTransfer.Assessment.Infrastructure.Persistence;

namespace WorkerTransfer.Assessment.Tests;

/// <summary>A real Postgres with this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("assessment")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string to the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = AssessmentDbContextFactory.DataSource(ConnectionString);
        await using var context = new AssessmentDbContext(
            (DbContextOptions<AssessmentDbContext>)AssessmentDbContextFactory.Configure(
                new DbContextOptionsBuilder<AssessmentDbContext>(), source).Options);

        await context.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>The collection that shares the one Postgres.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    /// <summary>Its name.</summary>
    public const string Name = "postgres";
}

/// <summary>The token shape this service verifies.</summary>
public static class TokenShape
{
    /// <summary>The private test key the test bench signs with.</summary>
    /// <remarks>
    /// Fixed in the tree, because it is valid only here. Rolled, it would make the
    /// suite depend on the machine it runs on.
    /// </remarks>
    public const string PrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>The matching public key the service gets.</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Names the key, so that a rotation allows two side by side.</summary>
    public const string KeyId = "test";

    private static readonly ECDsa Curve = Read();

    private static ECDsa Read()
    {
        var curve = ECDsa.Create();
        curve.ImportPkcs8PrivateKey(Convert.FromBase64String(PrivateKey), out _);
        return curve;
    }

    /// <summary>The issuer.</summary>
    public const string Issuer = "workertransfer";

    /// <summary>The audience.</summary>
    public const string Audience = "workertransfer";

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

/// <summary>A ledger the test controls.</summary>
/// <remarks>
/// It counts the questions, and that is the interesting number: the company side
/// has to ask at <em>every</em> read (ADR-0013), the person side at
/// <em>none</em> (ADR-0042 §2).
/// </remarks>
public sealed class ProbeLedger : IConsentGate
{
    /// <summary>Who has released something to whom — key "subject|company".</summary>
    public HashSet<string> Releases { get; } = [];

    /// <summary>How often it was asked.</summary>
    public int Questions { get; private set; }

    /// <summary>When true, the ledger says nothing at all.</summary>
    public bool Silent { get; set; }

    /// <summary>Releases or takes back.</summary>
    public void Set(Guid subject, Guid company, bool released)
    {
        var key = $"{subject}|{company}";

        if (released)
        {
            Releases.Add(key);
        }
        else
        {
            Releases.Remove(key);
        }
    }

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        (await MayEachSeeAsync([(subject, company)], cancellationToken))[0];

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MayEachSeeAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        Questions++;

        if (Silent)
        {
            throw new ConsentGateSilent("the ledger does not answer in the test");
        }

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. pairs.Select(pair => Releases.Contains($"{pair.Subject.Value}|{pair.Company.Value}"))]);
    }
}

/// <summary>Cases in memory.</summary>
/// <remarks>
/// Deliberately a store of its own instead of a database: the handler suite asks
/// about <em>decisions</em>, not about SQL. That the rows are really written is
/// measured by <c>AssessmentJourneyTests</c> against a real Postgres.
/// <para>
/// It hands back the same aggregate that was put in. That is kinder than reality
/// — a real repository hands out detached aggregates — and that is exactly why
/// the other suite stands beside it.
/// </para>
/// </remarks>
public sealed class ProbeCases : ICaseStore
{
    private readonly List<Case> _cases = [];

    /// <inheritdoc />
    public Task<Case?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cases.FirstOrDefault(caseItem => caseItem.Id == id));

    /// <inheritdoc />
    public Task<IReadOnlyList<Case>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Case>>([.. _cases.Where(v => v.Subject == subject)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Case>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Case>>([.. _cases.Where(v => v.Company == company)]);

    /// <inheritdoc />
    public Task SaveAsync(Case caseItem, CancellationToken cancellationToken = default)
    {
        if (!_cases.Contains(caseItem))
        {
            _cases.Add(caseItem);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        _cases.RemoveAll(caseItem => caseItem.Subject == subject);

        return Task.FromResult(0);
    }
}

/// <summary>An outbox that takes notes.</summary>
/// <remarks>
/// It records <em>what</em> was noted: an id and a kind. That nothing more fits
/// in is the outbox's contract (ADR-0025) and not a decision of this test.
/// </remarks>
public sealed class ProbeOutbox : Outbox.IOutbox
{
    /// <summary>What was noted, in the order of the calls.</summary>
    public List<(Guid Subject, string Kind)> Notes { get; } = [];

    /// <inheritdoc />
    public Task<Guid> NoteAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        Notes.Add((recipient.Value, kind));

        return Task.FromResult(Guid.NewGuid());
    }
}

/// <summary>A clock the test sets.</summary>
/// <remarks>
/// So that a deadline can pass without anybody waiting two days — and so that
/// "expired" becomes checkable without softening the limit.
/// </remarks>
public sealed class ProbeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan span) => _now += span;
}
