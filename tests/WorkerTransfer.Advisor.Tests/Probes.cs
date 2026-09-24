using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Infrastructure.Persistence;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>A real Postgres with this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("advisor")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string to the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = AdvisorDbContextFactory.DataSource(ConnectionString);
        await using var context = new AdvisorDbContext(
            (DbContextOptions<AdvisorDbContext>)AdvisorDbContextFactory.Configure(
                new DbContextOptionsBuilder<AdvisorDbContext>(), source).Options);

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
    /// <summary>The private test key (P-256, PKCS#8, base64).</summary>
    /// <remarks>
    /// Fixed in the tree, because it is valid only here: the service under test
    /// gets the matching public key. Rolled, it would make the journey depend on
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
        List<Claim> claims = [new(JwtRegisteredClaimNames.Sub, subject.ToString())];

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
/// It records <em>what</em> was written — that is the interesting question: a
/// stage comes about as a ledger event and nowhere else.
/// </remarks>
public sealed class ProbeLedger : IConsentGate
{
    /// <summary>Which capability holds for whom.</summary>
    public HashSet<string> Granted { get; } = [];

    /// <summary>What was granted, in the order of the calls.</summary>
    public List<string> Grants { get; } = [];

    /// <summary>What was revoked.</summary>
    public List<string> Revocations { get; } = [];

    /// <summary>The reasons that went along.</summary>
    public List<string> Reasons { get; } = [];

    /// <summary>How often stages were asked for. The read path must ask every time.</summary>
    public int Questions { get; private set; }

    /// <summary>When true, the ledger says nothing at all.</summary>
    public bool Silent { get; set; }

    /// <summary>When true, the ledger refuses every write.</summary>
    public bool Refuses { get; set; }

    /// <summary>Sets a stage the way the real ledger would hold it.</summary>
    public void SetStage(Guid subject, Guid company, Stage stage)
    {
        var tenant = new TenantId(company);

        Set(subject, StageCapabilities.Profile(tenant), stage >= Stage.Profile);
        Set(subject, StageCapabilities.Resume(tenant), stage >= Stage.Documents);
        Set(subject, StageCapabilities.CivilName(tenant), stage >= Stage.Person);
    }

    /// <summary>Sets "visible to all companies".</summary>
    public void SetPublic(Guid subject, bool on) =>
        Set(subject, StageCapabilities.ProfilePublic, on);

    private void Set(Guid subject, string capability, bool on)
    {
        var key = Key(subject, capability);

        if (on)
        {
            Granted.Add(key);
        }
        else
        {
            Granted.Remove(key);
        }
    }

    /// <summary>Who is acting right now — set in the test, from the token in operation.</summary>
    public Guid Self { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<Stage>> StagesAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        Questions++;

        if (Silent)
        {
            throw new ConsentGateSilent("the ledger does not answer in this test");
        }

        return Task.FromResult<IReadOnlyList<Stage>>([.. pairs.Select(Read)]);
    }

    private Stage Read((SubjectId Subject, TenantId Company) pair)
    {
        var subject = pair.Subject.Value;

        var profile = Granted.Contains(Key(subject, StageCapabilities.Profile(pair.Company)))
                     || Granted.Contains(Key(subject, StageCapabilities.ProfilePublic));

        if (!profile)
        {
            return Stage.None;
        }

        if (!Granted.Contains(Key(subject, StageCapabilities.Resume(pair.Company))))
        {
            return Stage.Profile;
        }

        return Granted.Contains(Key(subject, StageCapabilities.CivilName(pair.Company)))
            ? Stage.Person
            : Stage.Documents;
    }

    /// <inheritdoc />
    public Task GrantAsync(
        IReadOnlyList<string> capabilities, CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new ConsentGateSilent("the ledger does not answer in this test");
        }

        if (Refuses)
        {
            throw new ConsentRefused("a consent belongs to its subject");
        }

        foreach (var capability in capabilities)
        {
            Grants.Add(capability);
            Granted.Add(Key(Self, capability));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        IReadOnlyList<string> capabilities,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new ConsentGateSilent("the ledger does not answer in this test");
        }

        if (Refuses)
        {
            throw new ConsentRefused("a consent belongs to its subject");
        }

        foreach (var capability in capabilities)
        {
            Revocations.Add(capability);
            Reasons.Add(reason);
            Granted.Remove(Key(Self, capability));
        }

        return Task.CompletedTask;
    }

    private static string Key(Guid subject, string capability) => $"{subject}|{capability}";
}

/// <summary>A company lookup the test controls.</summary>
public sealed class ProbeCompanies : ICompanyLookup
{
    /// <summary>Which company has which domain.</summary>
    public Dictionary<Guid, string> Domains { get; } = [];

    /// <summary>How often it was asked — without an exclusion it must not be asked at all.</summary>
    public int Questions { get; private set; }

    /// <summary>When true, identity-service does not answer at all.</summary>
    public bool Silent { get; set; }

    /// <inheritdoc />
    public Task<CompanyCard?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        Questions++;

        if (Silent)
        {
            throw new LookupSilent("identity-service does not answer in this test");
        }

        return Task.FromResult(
            Domains.TryGetValue(company.Value, out var domain)
                ? new CompanyCard("Example Ltd", domain)
                : null);
    }
}

/// <summary>A person lookup the test controls.</summary>
public sealed class ProbePeople : IPersonLookup
{
    /// <summary>Who is called what.</summary>
    public Dictionary<Guid, PersonCard> Known { get; } = [];

    /// <summary>Who was asked about — in the order of the calls.</summary>
    public List<Guid> Asked { get; } = [];

    /// <inheritdoc />
    public Task<PersonCard?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        Asked.Add(subject.Value);

        return Task.FromResult(Known.GetValueOrDefault(subject.Value));
    }
}

/// <summary>A handover the test controls.</summary>
public sealed class ProbeHandover : ICaseHandover
{
    /// <summary>For whom a handover happened.</summary>
    public List<Guid> HandedOver { get; } = [];

    /// <summary>When true, transfer-service refuses.</summary>
    public bool Refuses { get; set; }

    /// <summary>When true, transfer-service does not answer at all.</summary>
    public bool Silent { get; set; }

    /// <summary>The id that comes back.</summary>
    public Guid Case { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public Task<Guid> HandOverAsync(
        SubjectId subject, string occasion, CancellationToken cancellationToken = default)
    {
        if (Silent)
        {
            throw new LookupSilent("transfer-service does not answer in this test");
        }

        if (Refuses)
        {
            throw new HandoverRefused("transfer-service refused with 403");
        }

        HandedOver.Add(subject.Value);

        return Task.FromResult(Case);
    }
}

/// <summary>Conversations in memory.</summary>
/// <remarks>
/// Deliberately a store of its own instead of a database: the handler suite
/// asks about <em>decisions</em>, not about SQL. That the rows really get
/// written is measured by <c>AdvisorJourneyTests</c> against a real Postgres.
/// <para>
/// It hands back the same aggregate that was put in. That is kinder than
/// reality — a real repository hands out detached aggregates — and exactly
/// that is why the other suite stands beside it.
/// </para>
/// </remarks>
public sealed class ProbeConversations : IConversationStore
{
    private readonly List<Conversation> _known = [];

    /// <inheritdoc />
    public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_known.FirstOrDefault(c => c.Id == id));

    /// <inheritdoc />
    public Task<Conversation?> GetRunningAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult(_known.FirstOrDefault(
            c => c.Subject == subject && c.Company == company && c.IsRunning));

    /// <inheritdoc />
    public Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        if (!_known.Contains(conversation))
        {
            _known.Add(conversation);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Conversation>>([.. _known.Where(c => c.Subject == subject)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Conversation>>([.. _known.Where(c => c.Company == company)]);

    /// <inheritdoc />
    public Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        _known.RemoveAll(c => c.Subject == subject);

        return Task.FromResult(0);
    }
}

/// <summary>Mandates in memory.</summary>
public sealed class ProbeMandates : Domain.Mandates.IMandateStore
{
    private readonly Dictionary<Guid, Domain.Mandates.Mandate> _known = [];

    /// <inheritdoc />
    public Task<Domain.Mandates.Mandate?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        Task.FromResult(_known.GetValueOrDefault(subject.Value));

    /// <inheritdoc />
    public Task SaveAsync(
        Domain.Mandates.Mandate mandate, CancellationToken cancellationToken = default)
    {
        _known[mandate.Subject.Value] = mandate;

        return Task.CompletedTask;
    }
}

/// <summary>An outbox that takes notes.</summary>
/// <remarks>
/// It records <em>what</em> was noted: an id and a kind. That no more fits in
/// is the outbox's contract (ADR-0025) and not a decision of this test.
/// </remarks>
public sealed class ProbeOutbox : Outbox.IOutbox
{
    /// <summary>What was noted, in the order of the calls.</summary>
    public List<(Guid Subject, string Kind)> Entries { get; } = [];

    /// <inheritdoc />
    public Task<Guid> NoteAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        Entries.Add((recipient.Value, kind));

        return Task.FromResult(Guid.NewGuid());
    }
}
