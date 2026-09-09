using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Verbindungen;
using WorkerTransfer.GitHub.Infrastructure.Persistence;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
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

        await using var quelle = GitHubDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new GitHubDbContext(
            GitHubDbContextFactory.Optionen(quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Ein eigener Kontext für Blicke in die Datenbank.</summary>
    public GitHubDbContext Kontext() =>
        new(GitHubDbContextFactory
            .Optionen(GitHubDbContextFactory.Datenquelle(ConnectionString)).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}

/// <summary>Ein GitHub, das der Test bestückt.</summary>
/// <summary>GitHubs Anmeldung, als Attrappe.</summary>
/// <remarks>
/// Sie ersetzt den ganzen Rücksprung: was ein Browser bei GitHub täte, ist hier
/// eine Zuordnung von Einmalcode auf Anmeldenamen.
/// </remarks>
public sealed class ProbeAnmeldung : IGitHubAnmeldung
{
    /// <summary>Welcher Code auf welchen Anmeldenamen führt.</summary>
    public Dictionary<string, string> Codes { get; } = new(StringComparer.Ordinal);

    /// <summary>Ob überhaupt Zugangsdaten hinterlegt sind.</summary>
    public bool Eingerichtet { get; set; } = true;

    /// <summary>Wenn gesetzt, schweigt GitHub.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Mit welchen Zuständen die Anmeldung begonnen wurde.</summary>
    public List<string> Zustaende { get; } = [];

    /// <inheritdoc />
    public Uri Anmeldeadresse(string zustand)
    {
        Zustaende.Add(zustand);
        return new Uri($"https://github.test/login/oauth/authorize?state={zustand}");
    }

    /// <inheritdoc />
    public Task<string?> AnmeldenamenAsync(
        string code, CancellationToken cancellationToken = default) =>
        Schweigt
            ? throw new GitHubSchweigt("github is unreachable")
            : Task.FromResult(Codes.GetValueOrDefault(code));
}

public sealed class ProbeGitHub : IGitHub
{
    /// <summary>Welche Gist-Beschreibungen es je Konto gibt.</summary>
    public Dictionary<string, List<string>> Gists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Welche Repositories es je Konto gibt.</summary>
    public Dictionary<string, List<Repository>> Repos { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wenn gesetzt, schweigt GitHub.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Welche Konten abgefragt wurden — für „hier wird nicht gefragt".</summary>
    public List<string> Gefragt { get; } = [];

    /// <summary>Legt einen Nachweisgist an, wie eine Person es täte.</summary>
    public void LegeGistAn(string login, string einmalzeichenfolge)
    {
        if (!Gists.TryGetValue(login, out var vorhandene))
        {
            vorhandene = [];
            Gists[login] = vorhandene;
        }

        vorhandene.Add(Verbindung.Gistbeschreibung(einmalzeichenfolge));
    }

    /// <inheritdoc />
    public Task<bool> HatNachweisgistAsync(
        string login, string einmalzeichenfolge, CancellationToken cancellationToken = default)
    {
        Stumm(login);

        var gesucht = Verbindung.Gistbeschreibung(einmalzeichenfolge);

        return Task.FromResult(
            Gists.TryGetValue(login, out var vorhandene) && vorhandene.Contains(gesucht));
    }

    /// <inheritdoc />
    public Task<Abzug> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default)
    {
        Stumm(login);

        return Task.FromResult(new Abzug(
            Repos.TryGetValue(login, out var vorhandene) ? vorhandene : [],
            SprachenVollstaendig));
    }

    /// <summary>Was die Probe über die Vollständigkeit der Sprachen meldet.</summary>
    public bool SprachenVollstaendig { get; set; } = true;

    private void Stumm(string login)
    {
        Gefragt.Add(login);

        if (Schweigt)
        {
            throw new GitHubSchweigt("Probe: GitHub schweigt.");
        }
    }
}

/// <summary>Ein Ledger, der antwortet, was der Test bestimmt.</summary>
public sealed class Probeledger : IEinwilligungstor
{
    /// <summary>Wessen Verbindung freigegeben ist.</summary>
    public HashSet<Guid> Frei { get; } = [];

    /// <summary>Wenn gesetzt, schweigt der Ledger.</summary>
    public bool Schweigt { get; set; }

    /// <inheritdoc />
    public Task<bool> DarfGezeigtWerdenAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new EinwilligungSchweigt("Probe: der Ledger schweigt.");
        }

        return Task.FromResult(Frei.Contains(wer.Value));
    }
}

/// <summary>Die Tokenform, die dieser Dienst prüft.</summary>
public static class Tokenform
{
    public const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    public const string Issuer = "workertransfer";
    public const string Audience = "workertransfer";

    public static string Person(Guid wer)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, wer.ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Geheimnis)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
