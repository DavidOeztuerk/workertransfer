using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Infrastructure.Persistence;

namespace WorkerTransfer.Scout.Tests;

/// <summary>A real Postgres with this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("scout")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string to the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = ScoutDbContextFactory.DataSource(ConnectionString);
        await using var context = new ScoutDbContext(
            (DbContextOptions<ScoutDbContext>)ScoutDbContextFactory.Configure(
                new DbContextOptionsBuilder<ScoutDbContext>(), source).Options);

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
    /// Fixed in the tree, because it is valid only here: the service under test
    /// gets the matching public key. Rolled, it would make the suite depend on
    /// the machine it runs on.
    /// </remarks>
    public const string PrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>The matching public key (SPKI, base64).</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Names the key, so that a rotation allows two side by side.</summary>
    public const string KeyId = "test";

    /// <summary>The issuer.</summary>
    public const string Issuer = "workertransfer";

    /// <summary>The audience.</summary>
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

/// <summary>A profile search the test controls.</summary>
/// <remarks>
/// It records <em>with what</em> it was asked — that is the interesting
/// question: searching may only happen over what was named.
/// </remarks>
public sealed class ProbeSearch : IProfileSearch
{
    /// <summary>The profiles, in exactly this order.</summary>
    public List<ProfileHit> Stock { get; } = [];

    /// <summary>What was last searched with.</summary>
    public SearchFilter? LastFilter { get; private set; }

    /// <summary>How often a search ran.</summary>
    public int Searches { get; private set; }

    /// <summary>If true, profile-service does not answer at all.</summary>
    public bool Silent { get; set; }

    /// <summary>
    /// If true, the internal door is shut — it answers 404 instead of 401.
    /// </summary>
    /// <remarks>
    /// The real adapter turns that into a <see cref="ProfileSearchSilent"/> and
    /// expressly not into an empty page. This probe reproduces the adapter's
    /// decision, not its HTTP traffic — what is checked is what the service does
    /// with such a failure.
    /// </remarks>
    public bool Shut { get; set; }

    /// <inheritdoc />
    public Task<ProfileHitPage> SearchAsync(
        SearchFilter filter,
        int pageLength,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        Searches++;
        LastFilter = filter;

        if (Silent)
        {
            throw new ProfileSearchSilent("profile-service does not answer in the test");
        }

        if (Shut)
        {
            throw new ProfileSearchSilent(
                "profile-service answered 404 to the search — the shared secret "
                + "is refused or the door is closed");
        }

        // Just like the real service: filtered over NAMED words, and as an OR —
        // whoever names one is in. Nothing else is searchable here.
        var hits = Stock
            .Where(profile => filter.NamedWords.Count == 0
                             || filter.NamedWords.Any(
                                 word => profile.Named.Contains(
                                     word, StringComparer.OrdinalIgnoreCase)))
            .Where(profile => filter.Location.Length == 0
                             || profile.Location.Contains(filter.Location, StringComparison.OrdinalIgnoreCase))
            .Where(profile => !filter.RemoteOnly || profile.RemoteOk)
            .Take(pageLength)
            .ToArray();

        return Task.FromResult(new ProfileHitPage(hits, null));
    }

    /// <inheritdoc />
    public Task<ProfileHit?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new ProfileSearchSilent("profile-service does not answer in the test");
        }

        return Task.FromResult(Stock.FirstOrDefault(profile => profile.Subject == subject));
    }
}

/// <summary>A ledger the test controls.</summary>
public sealed class ProbeLedger : IConsentGate
{
    /// <summary>Who is visible to which company.</summary>
    public HashSet<(Guid Subject, Guid Company)> Released { get; } = [];

    /// <summary>How often it was asked. The read path has to ask every time.</summary>
    public int Questions { get; private set; }

    /// <summary>If true, the ledger says nothing at all.</summary>
    public bool Silent { get; set; }

    /// <summary>If set, the ledger answers with as many rows as stand here.</summary>
    /// <remarks>
    /// For the counter-probe: an answer that does not fit the questions must not
    /// be guessed.
    /// </remarks>
    public int? AnswersWithLength { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MaySeeAllAsync(
        IReadOnlyList<SubjectId> subjects,
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        Questions++;

        if (Silent)
        {
            throw new ConsentGateSilent("the ledger does not answer in the test");
        }

        if (AnswersWithLength is { } wrong)
        {
            return Task.FromResult<IReadOnlyList<bool>>([.. Enumerable.Repeat(true, wrong)]);
        }

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. subjects.Select(subject => Released.Contains((subject.Value, company.Value)))]);
    }
}

/// <summary>Evidence the test controls.</summary>
public sealed class ProbeEvidence : IEvidenceSource
{
    /// <summary>What is available about whom.</summary>
    public Dictionary<Guid, EvidenceSheet> Stock { get; } = [];

    /// <summary>Who was asked about — in the order of the calls.</summary>
    public List<Guid> Asked { get; } = [];

    /// <inheritdoc />
    public Task<EvidenceSheet> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        Asked.Add(subject.Value);

        return Task.FromResult(
            Stock.TryGetValue(subject.Value, out var sheet)
                ? sheet
                : new EvidenceSheet([], EvidenceState.NoneReleased));
    }
}

/// <summary>Job statements the test controls.</summary>
public sealed class ProbeJobs : IJobLookup
{
    /// <summary>What is available about which advert.</summary>
    public Dictionary<Guid, JobStatement> Stock { get; } = [];

    /// <summary>How often it was asked — ONCE per page, not per row.</summary>
    public int Questions { get; private set; }

    /// <summary>If true, jobs-service does not answer at all.</summary>
    public bool Silent { get; set; }

    /// <inheritdoc />
    public Task<JobStatement?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        Questions++;

        return Silent
            ? throw new JobLookupSilent("jobs-service does not answer in the test")
            : Task.FromResult(Stock.GetValueOrDefault(job));
    }
}

/// <summary>A drafter that takes notes instead of asking.</summary>
public sealed class ProbeDrafter : IDrafter
{
    /// <summary>What would have gone out last — that is the interesting question.</summary>
    public ApproachSituation? Last { get; private set; }

    /// <summary>If true, no provider is set up.</summary>
    public bool Missing { get; set; }

    /// <inheritdoc />
    public Task<string> DraftAsync(
        ApproachSituation situation, TenantId company, CancellationToken cancellationToken = default)
    {
        Last = situation;

        return Missing
            ? throw new DraftUnavailable("no provider is set up in the test")
            : Task.FromResult("Hello, we are looking for somebody for distributed systems.");
    }
}
