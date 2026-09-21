using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Domain.Hit;
using WorkerTransfer.Scout.Infrastructure.Persistence;

namespace WorkerTransfer.Scout.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _behaelter = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("scout")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql-Verbindungszeichenfolge zur gewanderten Datenbank.</summary>
    public string ConnectionString => _behaelter.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _behaelter.StartAsync();

        await using var source = ScoutDbContextFactory.DataSource(ConnectionString);
        await using var context = new ScoutDbContext(
            (DbContextOptions<ScoutDbContext>)ScoutDbContextFactory.Configure(
                new DbContextOptionsBuilder<ScoutDbContext>(), source).Options);

        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _behaelter.DisposeAsync();
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

/// <summary>Eine Profilsuche, die der Test steuert.</summary>
/// <remarks>
/// Sie hält fest, <em>womit</em> gefragt wurde — das ist die interessante
/// Frage: gesucht werden darf nur über Genanntes.
/// </remarks>
public sealed class Probesuche : IProfileSearch
{
    /// <summary>Die Profile, in genau dieser Reihenfolge.</summary>
    public List<ProfileHit> Bestand { get; } = [];

    /// <summary>Womit zuletzt gesucht wurde.</summary>
    public SearchFilter? LetzterFilter { get; private set; }

    /// <summary>Wie oft gesucht wurde.</summary>
    public int Searches { get; private set; }

    /// <summary>Wenn wahr, antwortet profile-service gar nicht.</summary>
    public bool Schweigt { get; set; }

    /// <summary>
    /// Wenn wahr, ist die interne Tür zu — sie antwortet 404 statt 401.
    /// </summary>
    /// <remarks>
    /// Der echte Adapter macht daraus ein <see cref="ProfilsucheSchweigt"/> und
    /// ausdrücklich keine leere Seite. Diese Probe bildet die Entscheidung des
    /// Adapters nach, nicht seinen HTTP-Verkehr — geprüft wird, was der Dienst
    /// mit einem solchen Fehlschlag macht.
    /// </remarks>
    public bool Verschlossen { get; set; }

    /// <inheritdoc />
    public Task<ProfileHitPage> SearchAsync(
        SearchFilter filter,
        int seitenlaenge,
        string? zeiger,
        CancellationToken cancellationToken = default)
    {
        Searches++;
        LetzterFilter = filter;

        if (Schweigt)
        {
            throw new ProfileSearchSilent("profile-service antwortet im Test nicht");
        }

        if (Verschlossen)
        {
            throw new ProfileSearchSilent(
                "profile-service answered 404 to the search — the shared secret "
                + "is refused or the door is closed");
        }

        // Genau wie der echte Dienst: gefiltert wird ueber GENANNTE Worte, und
        // als ODER — wer eines nennt, ist dabei. Nichts anderes ist hier
        // durchsuchbar.
        var hit = Bestand
            .Where(profile => filter.GenannteWorte.Count == 0
                             || filter.GenannteWorte.Any(
                                 word => profile.Genannt.Contains(
                                     word, StringComparer.OrdinalIgnoreCase)))
            .Where(profile => filter.Ort.Length == 0
                             || profile.Ort.Contains(filter.Ort, StringComparison.OrdinalIgnoreCase))
            .Where(profile => !filter.NurRemote || profile.RemoteMoeglich)
            .Take(seitenlaenge)
            .ToArray();

        return Task.FromResult(new ProfileHitPage(hit, null));
    }

    /// <inheritdoc />
    public Task<ProfileHit?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new ProfileSearchSilent("profile-service antwortet im Test nicht");
        }

        return Task.FromResult(Bestand.FirstOrDefault(profile => profile.Wer == subject));
    }
}

/// <summary>Ein Ledger, den der Test steuert.</summary>
public sealed class Probetor : IConsentGate
{
    /// <summary>Wer für welches Unternehmen sichtbar ist.</summary>
    public HashSet<(Guid Wer, Guid Firma)> Frei { get; } = [];

    /// <summary>Wie oft gefragt wurde. Der Lesepfad muss jedes Mal fragen.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, sagt der Ledger gar nichts.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Wenn gesetzt, antwortet der Ledger mit so vielen Zeilen wie hier steht.</summary>
    /// <remarks>
    /// Für die Gegenprobe: eine Antwort, die nicht zu den Fragen passt, darf
    /// nicht geraten werden.
    /// </remarks>
    public int? AntwortetMitLaenge { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MaySeeAllAsync(
        IReadOnlyList<SubjectId> subject,
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new ConsentGateSilent("der Ledger antwortet im Test nicht");
        }

        if (AntwortetMitLaenge is { } falsch)
        {
            return Task.FromResult<IReadOnlyList<bool>>([.. Enumerable.Repeat(true, falsch)]);
        }

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. subject.Select(einer => Frei.Contains((einer.Value, company.Value)))]);
    }
}

/// <summary>Belege, die der Test steuert.</summary>
public sealed class Probebelege : IEvidenceSource
{
    /// <summary>Was zu wem vorliegt.</summary>
    public Dictionary<Guid, EvidenceSheet> Bestand { get; } = [];

    /// <summary>Wen zuletzt gefragt wurde — in der Reihenfolge der Aufrufe.</summary>
    public List<Guid> Gefragt { get; } = [];

    /// <inheritdoc />
    public Task<EvidenceSheet> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        Gefragt.Add(subject.Value);

        return Task.FromResult(
            Bestand.TryGetValue(subject.Value, out var bogen)
                ? bogen
                : new EvidenceSheet([], EvidenceState.KeineFreigegeben));
    }
}

/// <summary>Stellenaussagen, die der Test steuert.</summary>
public sealed class Probestellen : IJobs
{
    /// <summary>Was zu welcher Anzeige vorliegt.</summary>
    public Dictionary<Guid, JobStatement> Bestand { get; } = [];

    /// <summary>Wie oft gefragt wurde — EINMAL je Seite, nicht je Zeile.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, antwortet jobs-service gar nicht.</summary>
    public bool Schweigt { get; set; }

    /// <inheritdoc />
    public Task<JobStatement?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        Fragen++;

        return Schweigt
            ? throw new JobLookupSilent("jobs-service antwortet im Test nicht")
            : Task.FromResult(Bestand.GetValueOrDefault(job));
    }
}

/// <summary>Ein Entwerfer, der mitschreibt statt zu fragen.</summary>
public sealed class Probeentwerfer : IDrafter
{
    /// <summary>Was zuletzt hinausgegangen wäre — das ist die interessante Frage.</summary>
    public Ansprachelage? Letzte { get; private set; }

    /// <summary>Wenn wahr, ist kein Anbieter eingerichtet.</summary>
    public bool Fehlt { get; set; }

    /// <inheritdoc />
    public Task<string> DraftAsync(
        Ansprachelage lage, CancellationToken cancellationToken = default)
    {
        Letzte = lage;

        return Fehlt
            ? throw new AnspracheNichtVerfuegbar("im Test ist kein Anbieter eingerichtet")
            : Task.FromResult("Hallo, wir suchen jemanden für verteilte Systeme.");
    }
}
