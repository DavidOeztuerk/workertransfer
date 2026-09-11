using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.Assessment.Infrastructure.Persistence;

namespace WorkerTransfer.Assessment.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _behaelter = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("assessment")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql-Verbindungszeichenfolge zur gewanderten Datenbank.</summary>
    public string ConnectionString => _behaelter.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _behaelter.StartAsync();

        await using var quelle = AssessmentDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new AssessmentDbContext(
            (DbContextOptions<AssessmentDbContext>)AssessmentDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<AssessmentDbContext>(), quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _behaelter.DisposeAsync();
}

/// <summary>Die Sammlung, die sich das eine Postgres teilt.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    /// <summary>Ihr Name.</summary>
    public const string Name = "postgres";
}

/// <summary>Die Tokenform, die dieser Dienst prüft.</summary>
public static class Tokenform
{
    /// <summary>Das Geheimnis, mit dem der Prüfstand unterschreibt.</summary>
    public const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";

    /// <summary>Der Aussteller.</summary>
    public const string Issuer = "workertransfer";

    /// <summary>Das Publikum.</summary>
    public const string Audience = "workertransfer";

    /// <summary>Ein Token für eine Person, die für sich selbst handelt.</summary>
    public static string Person(Guid wer) => Baue(wer, firma: null);

    /// <summary>Ein Token für jemanden, der für ein Unternehmen handelt.</summary>
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

/// <summary>Ein Ledger, den der Test steuert.</summary>
/// <remarks>
/// Er zählt die Fragen, und das ist die interessante Zahl: die Firmenseite muss
/// bei <em>jedem</em> Lesen fragen (ADR-0013), die Personenseite bei
/// <em>keinem</em> (ADR-0042 §2).
/// </remarks>
public sealed class Probetor : IEinwilligungstor
{
    /// <summary>Wer wem etwas freigegeben hat — Schlüssel „wer|firma".</summary>
    public HashSet<string> Freigaben { get; } = [];

    /// <summary>Wie oft gefragt wurde.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, sagt der Ledger gar nichts.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Gibt frei oder nimmt zurück.</summary>
    public void Stelle(Guid wer, Guid firma, bool frei)
    {
        var schluessel = $"{wer}|{firma}";

        if (frei)
        {
            Freigaben.Add(schluessel);
        }
        else
        {
            Freigaben.Remove(schluessel);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DarfSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default) =>
        (await DuerfenSehenAsync([(wer, firma)], cancellationToken))[0];

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> DuerfenSehenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht");
        }

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. paare.Select(paar => Freigaben.Contains($"{paar.Wer.Value}|{paar.Firma.Value}"))]);
    }
}

/// <summary>Vorgänge im Arbeitsspeicher.</summary>
/// <remarks>
/// Bewusst ein eigener Speicher statt einer Datenbank: die Handlerreihe fragt
/// nach <em>Entscheidungen</em>, nicht nach SQL. Dass die Zeilen wirklich
/// geschrieben werden, misst <c>ArbeitsprobenreiseTests</c> gegen ein echtes
/// Postgres.
/// <para>
/// Er gibt dasselbe Aggregat zurück, das hineingelegt wurde. Das ist gutmütiger
/// als die Wirklichkeit — ein echtes Repository gibt losgelöste Aggregate heraus
/// — und genau deshalb steht die andere Reihe daneben.
/// </para>
/// </remarks>
public sealed class Probevorgaenge : IVorgangsspeicher
{
    private readonly List<Vorgang> _bestand = [];

    /// <inheritdoc />
    public Task<Vorgang?> HoleAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.FirstOrDefault(vorgang => vorgang.Id == id));

    /// <inheritdoc />
    public Task<IReadOnlyList<Vorgang>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Vorgang>>([.. _bestand.Where(v => v.Wer == wer)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Vorgang>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Vorgang>>([.. _bestand.Where(v => v.Firma == firma)]);

    /// <inheritdoc />
    public Task SichereAsync(Vorgang vorgang, CancellationToken cancellationToken = default)
    {
        if (!_bestand.Contains(vorgang))
        {
            _bestand.Add(vorgang);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default)
    {
        _bestand.RemoveAll(vorgang => vorgang.Wer == wer);

        return Task.FromResult(0);
    }
}

/// <summary>Ein Postausgang, der mitschreibt.</summary>
/// <remarks>
/// Er hält fest, <em>was</em> vermerkt wurde: eine Kennung und eine Art. Dass
/// mehr nicht hineinpasst, ist der Vertrag der Outbox (ADR-0025) und keine
/// Entscheidung dieses Tests.
/// </remarks>
public sealed class Probeausgang : Outbox.IOutbox
{
    /// <summary>Was vermerkt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<(Guid Wer, string Art)> Vermerke { get; } = [];

    /// <inheritdoc />
    public Task<Guid> VermerkeAsync(
        SubjectId empfaenger, string art, CancellationToken cancellationToken = default)
    {
        Vermerke.Add((empfaenger.Value, art));

        return Task.FromResult(Guid.NewGuid());
    }
}

/// <summary>Eine Uhr, die der Test stellt.</summary>
/// <remarks>
/// Damit eine Frist verstreichen kann, ohne dass jemand zwei Tage wartet — und
/// damit „abgelaufen" prüfbar wird, ohne die Grenze aufzuweichen.
/// </remarks>
public sealed class Probeuhr(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _jetzt = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _jetzt;

    /// <summary>Stellt die Uhr vor.</summary>
    public void Weiter(TimeSpan spanne) => _jetzt += spanne;
}
