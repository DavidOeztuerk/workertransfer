using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
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

        await using var source = AssessmentDbContextFactory.DataSource(ConnectionString);
        await using var context = new AssessmentDbContext(
            (DbContextOptions<AssessmentDbContext>)AssessmentDbContextFactory.Configure(
                new DbContextOptionsBuilder<AssessmentDbContext>(), source).Options);

        await context.Database.MigrateAsync();
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
    /// <summary>Der private Testschlüssel, mit dem der Prüfstand unterschreibt.</summary>
    /// <remarks>
    /// Fest im Baum, weil er nur hier gilt. Gewürfelt machte er die Reise von
    /// der Maschine abhängig, auf der sie läuft.
    /// </remarks>
    public const string PrivatSchluessel = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>Der passende öffentliche Schlüssel, den der Dienst bekommt.</summary>
    public const string OeffentlichSchluessel = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Benennt den Schlüssel, damit ein Wechsel zwei nebeneinander erlaubt.</summary>
    public const string Kennung = "test";

    private static readonly ECDsa Kurve = Lies();

    private static ECDsa Lies()
    {
        var kurve = ECDsa.Create();
        kurve.ImportPkcs8PrivateKey(Convert.FromBase64String(PrivatSchluessel), out _);
        return kurve;
    }

    /// <summary>Der Aussteller.</summary>
    public const string Issuer = "workertransfer";

    /// <summary>Das Publikum.</summary>
    public const string Audience = "workertransfer";

    /// <summary>Ein Token für eine Person, die für sich selbst handelt.</summary>
    public static string Person(Guid subject) => Build(subject, company: null);

    /// <summary>Ein Token für jemanden, der für ein Unternehmen handelt.</summary>
    public static string Firma(Guid subject, Guid company) => Build(subject, company);

    private static string Build(Guid subject, Guid? company)
    {
        List<Claim> ansprueche = [new(JwtRegisteredClaimNames.Sub, subject.ToString())];

        if (company is { } tenant)
        {
            ansprueche.Add(new Claim("tenant", tenant.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: ansprueche,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(Kurve) { KeyId = Kennung },
                SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>Ein Ledger, den der Test steuert.</summary>
/// <remarks>
/// Er zählt die Fragen, und das ist die interessante Zahl: die Firmenseite muss
/// bei <em>jedem</em> Lesen fragen (ADR-0013), die Personenseite bei
/// <em>keinem</em> (ADR-0042 §2).
/// </remarks>
public sealed class Probetor : IConsentGate
{
    /// <summary>Wer wem etwas freigegeben hat — Schlüssel „wer|firma".</summary>
    public HashSet<string> Freigaben { get; } = [];

    /// <summary>Wie oft gefragt wurde.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, sagt der Ledger gar nichts.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Gibt frei oder nimmt zurück.</summary>
    public void Job(Guid subject, Guid company, bool frei)
    {
        var key = $"{subject}|{company}";

        if (frei)
        {
            Freigaben.Add(key);
        }
        else
        {
            Freigaben.Remove(key);
        }
    }

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        (await MayEachSeeAsync([(subject, company)], cancellationToken))[0];

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MayEachSeeAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new ConsentGateSilent("der Ledger antwortet im Test nicht");
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
public sealed class Probevorgaenge : ICaseStore
{
    private readonly List<Case> _bestand = [];

    /// <inheritdoc />
    public Task<Case?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.FirstOrDefault(caseItem => caseItem.Id == id));

    /// <inheritdoc />
    public Task<IReadOnlyList<Case>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Case>>([.. _bestand.Where(v => v.Wer == subject)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Case>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Case>>([.. _bestand.Where(v => v.Firma == company)]);

    /// <inheritdoc />
    public Task SaveAsync(Case caseItem, CancellationToken cancellationToken = default)
    {
        if (!_bestand.Contains(caseItem))
        {
            _bestand.Add(caseItem);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        _bestand.RemoveAll(caseItem => caseItem.Wer == subject);

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
    public Task<Guid> NoteAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        Vermerke.Add((recipient.Value, kind));

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
