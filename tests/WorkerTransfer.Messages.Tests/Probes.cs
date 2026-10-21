using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Messages.Infrastructure.Persistence;

namespace WorkerTransfer.Messages.Tests;

/// <summary>A real Postgres with this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("messages")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string to the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = MessagesDbContextFactory.DataSource(ConnectionString);
        await using var context = new MessagesDbContext(
            (DbContextOptions<MessagesDbContext>)MessagesDbContextFactory.Configure(
                new DbContextOptionsBuilder<MessagesDbContext>(), source).Options);

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

/// <summary>applications-service, as the test wants it to answer.</summary>
/// <remarks>
/// It answers for every caller; the real one shows a process only to its two
/// parties. That the stranger still gets 404 here is therefore this service's
/// own side resolution — measured, not borrowed.
/// </remarks>
public sealed class ProbeProcess : IProcessLookup
{
    /// <summary>The applications that exist.</summary>
    public Dictionary<Guid, ProcessParties> Applications { get; } = [];

    /// <summary>When true, applications-service says nothing at all.</summary>
    public bool Silent { get; set; }

    /// <summary>How often it was asked.</summary>
    public int Questions { get; private set; }

    /// <inheritdoc />
    public Task<ProcessParties?> PartiesAsync(
        string kind, Guid reference, CancellationToken cancellationToken = default)
    {
        Questions++;

        if (Silent)
        {
            throw new ProcessLookupSilent("applications-service does not answer in the test");
        }

        return Task.FromResult(
            kind == "application" && Applications.TryGetValue(reference, out var parties) ? parties : null);
    }
}

/// <summary>identity-service's member list.</summary>
public sealed class ProbeMembers : ICompanyMemberLookup
{
    /// <summary>Members per company.</summary>
    public Dictionary<Guid, List<Guid>> Members { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<SubjectId>> MembersAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubjectId>>(
            Members.TryGetValue(company.Value, out var members)
                ? [.. members.Select(member => new SubjectId(member))]
                : []);
}

/// <summary>A model that writes what the test says — and remembers what it read.</summary>
public sealed class ProbeDrafter : IReplyDrafter
{
    /// <summary>What it answers.</summary>
    public string Answer { get; set; } = "Vielen Dank, wir melden uns.";

    /// <summary>When true, no provider is set up.</summary>
    public bool Unavailable { get; set; }

    /// <summary>What it was given, in order.</summary>
    public List<ReplyContext> Read { get; } = [];

    /// <inheritdoc />
    public Task<string> DraftAsync(
        ReplyContext context, SubjectId requestedBy, TenantId? company,
        CancellationToken cancellationToken = default)
    {
        if (Unavailable)
        {
            throw new DraftUnavailable(DraftUnavailable.NoProvider);
        }

        Read.Add(context);
        return Task.FromResult(Answer);
    }
}

/// <summary>A ledger the test controls.</summary>
/// <remarks>
/// It counts the questions, and that is the interesting number: the company side
/// has to ask at <em>every</em> read (ADR-0013), the person side at
/// <em>none</em> (ADR-0047).
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

/// <summary>identity-service's role lookup, answered by the test — recruiter by default.</summary>
public sealed class RoleProbe : ICompanyRoles
{
    /// <summary>What it answers.</summary>
    public CompanyRole Answer { get; set; } = CompanyRole.Recruiter;

    /// <inheritdoc />
    public Task<CompanyRole> RoleAsync(Guid subject, Guid company, CancellationToken cancellationToken = default) =>
        Task.FromResult(Answer);
}

/// <summary>The ledger's write side — records every release and feeds the read side.</summary>
/// <remarks>
/// A release written here is seen by <see cref="ProbeLedger"/> at once, the
/// way the real ledger answers the next read.
/// </remarks>
public sealed class ProbeReleases(ProbeLedger ledger) : IReleaseWriter
{
    /// <summary>What was released, in order.</summary>
    public List<(Guid Subject, Guid Company)> Granted { get; } = [];

    /// <summary>When true, the ledger does not answer.</summary>
    public bool Silent { get; set; }

    /// <inheritdoc />
    public Task GrantProfileAsync(SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new ConsentGateSilent("the ledger does not answer in the test");
        }

        Granted.Add((subject.Value, company.Value));
        ledger.Set(subject.Value, company.Value, true);
        return Task.CompletedTask;
    }
}

/// <summary>The person's own facts, as the test sets them.</summary>
public sealed class ProbeOwnFacts : IOwnFactsLookup
{
    /// <summary>What it answers.</summary>
    public OwnFacts Facts { get; set; } = new(null, false, null, false);

    /// <inheritdoc />
    public Task<OwnFacts> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Facts);
}

/// <summary>An advisor that remembers what it read.</summary>
public sealed class ProbeAdvisor : IAdvisor
{
    /// <summary>What it was given.</summary>
    public List<AdviceContext> Read { get; } = [];

    /// <inheritdoc />
    public Task<string> AdviseAsync(AdviceContext context, SubjectId requestedBy, CancellationToken cancellationToken = default)
    {
        Read.Add(context);
        return Task.FromResult("Das Angebot beginnt vor deinem frühesten Wechsel (deine Kündigungsfrist).");
    }
}
