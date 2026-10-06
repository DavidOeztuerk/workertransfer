using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.GitHub.Infrastructure.Persistence;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>A real Postgres carrying this service's schema.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("github")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = GitHubDbContextFactory.DataSource(ConnectionString);
        await using var context = new GitHubDbContext(
            GitHubDbContextFactory.Options(source).Options);

        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A context of its own, for looking into the database.</summary>
    public GitHubDbContext Context() =>
        new(GitHubDbContextFactory
            .Options(GitHubDbContextFactory.DataSource(ConnectionString)).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>GitHub's sign-in, as a fake.</summary>
/// <remarks>
/// It replaces the whole return: what a browser would do at GitHub is a mapping
/// from one-time code to login name here.
/// </remarks>
public sealed class ProbeSignIn : IGitHubSignIn
{
    /// <summary>Which code leads to which login name.</summary>
    public Dictionary<string, string> Codes { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether any credentials are stored at all.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>When set, GitHub stays silent.</summary>
    public bool IsSilent { get; set; }

    /// <summary>Which states the sign-in was begun with.</summary>
    public List<string> States { get; } = [];

    /// <inheritdoc />
    public Uri AuthorizeUrl(string state)
    {
        States.Add(state);
        return new Uri($"https://github.test/login/oauth/authorize?state={state}");
    }

    /// <inheritdoc />
    public Task<string?> LoginNameAsync(
        string code, CancellationToken cancellationToken = default) =>
        IsSilent
            ? throw new GitHubSilent("github is unreachable")
            : Task.FromResult(Codes.GetValueOrDefault(code));
}

/// <summary>A GitHub the test stocks.</summary>
public sealed class ProbeGitHub : IGitHub
{
    /// <summary>Which gist descriptions exist per account.</summary>
    public Dictionary<string, List<string>> Gists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which repositories exist per account.</summary>
    public Dictionary<string, List<Repository>> Repos { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, GitHub stays silent.</summary>
    public bool IsSilent { get; set; }

    /// <summary>Which accounts were asked for — for "nothing is asked here".</summary>
    public List<string> Asked { get; } = [];

    /// <summary>Creates a proof gist, as a person would.</summary>
    public void CreateGist(string login, string nonce)
    {
        if (!Gists.TryGetValue(login, out var existing))
        {
            existing = [];
            Gists[login] = existing;
        }

        existing.Add(Connection.GistDescription(nonce));
    }

    /// <inheritdoc />
    public Task<bool> HasProofGistAsync(
        string login, string nonce, CancellationToken cancellationToken = default)
    {
        Silence(login);

        var wanted = Connection.GistDescription(nonce);

        return Task.FromResult(
            Gists.TryGetValue(login, out var existing) && existing.Contains(wanted));
    }

    /// <inheritdoc />
    public Task<Snapshot> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default)
    {
        Silence(login);

        return Task.FromResult(new Snapshot(
            Repos.TryGetValue(login, out var existing) ? existing : [],
            LanguagesComplete));
    }

    /// <summary>What the probe reports about the completeness of the languages.</summary>
    public bool LanguagesComplete { get; set; } = true;

    private void Silence(string login)
    {
        Asked.Add(login);

        if (IsSilent)
        {
            throw new GitHubSilent("Probe: GitHub is silent.");
        }
    }
}

/// <summary>A ledger that answers what the test decides.</summary>
public sealed class ProbeLedger : IConsentGate
{
    /// <summary>Whose connection is released.</summary>
    public HashSet<Guid> Released { get; } = [];

    /// <summary>When set, the ledger stays silent.</summary>
    public bool IsSilent { get; set; }

    /// <inheritdoc />
    public Task<bool> MayBeShownAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (IsSilent)
        {
            throw new ConsentGateSilent("Probe: the ledger is silent.");
        }

        return Task.FromResult(Released.Contains(subject.Value));
    }
}

/// <summary>The token shape this service verifies.</summary>
public static class TokenShape
{
    /// <summary>The private test key (P-256, PKCS#8, base64).</summary>
    /// <remarks>
    /// Fixed in the tree because it applies only here: the service under test
    /// gets the matching public key. Rolled, it would make the journey depend on
    /// the machine it runs on.
    /// </remarks>
    public const string PrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>The matching public key (SPKI, base64).</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Names the key, so a change may hold two of them side by side.</summary>
    public const string KeyId = "test";

    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    private static readonly ECDsa Curve = Read();

    private static ECDsa Read()
    {
        var curve = ECDsa.Create();
        curve.ImportPkcs8PrivateKey(Convert.FromBase64String(PrivateKey), out _);
        return curve;
    }

    public static string Person(Guid subject)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience,
            // Like a real token from identity-service: the revocation check refuses
            // one without jti and iat.
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64)
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(Curve) { KeyId = KeyId },
                SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
