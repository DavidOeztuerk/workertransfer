using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Suchen;
using WorkerTransfer.Scout.Domain.Treffer;
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

        await using var quelle = ScoutDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new ScoutDbContext(
            (DbContextOptions<ScoutDbContext>)ScoutDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<ScoutDbContext>(), quelle).Options);

        await kontext.Database.MigrateAsync();
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
    public const string Geheimnis = "test-secret-with-at-least-thirty-two-bytes-xx";
    public const string Issuer = "workertransfer";
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

/// <summary>Eine Profilsuche, die der Test steuert.</summary>
/// <remarks>
/// Sie hält fest, <em>womit</em> gefragt wurde — das ist die interessante
/// Frage: gesucht werden darf nur über Genanntes.
/// </remarks>
public sealed class Probesuche : IProfilsuche
{
    /// <summary>Die Profile, in genau dieser Reihenfolge.</summary>
    public List<Profilfund> Bestand { get; } = [];

    /// <summary>Womit zuletzt gesucht wurde.</summary>
    public Suchfilter? LetzterFilter { get; private set; }

    /// <summary>Wie oft gesucht wurde.</summary>
    public int Suchen { get; private set; }

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
    public Task<Profilfundseite> SucheAsync(
        Suchfilter filter,
        int seitenlaenge,
        string? zeiger,
        CancellationToken cancellationToken = default)
    {
        Suchen++;
        LetzterFilter = filter;

        if (Schweigt)
        {
            throw new ProfilsucheSchweigt("profile-service antwortet im Test nicht");
        }

        if (Verschlossen)
        {
            throw new ProfilsucheSchweigt(
                "profile-service answered 404 to the search — the shared secret "
                + "is refused or the door is closed");
        }

        // Genau wie der echte Dienst: gefiltert wird ueber GENANNTE Worte, und
        // als ODER — wer eines nennt, ist dabei. Nichts anderes ist hier
        // durchsuchbar.
        var treffer = Bestand
            .Where(profil => filter.GenannteWorte.Count == 0
                             || filter.GenannteWorte.Any(
                                 wort => profil.Genannt.Contains(
                                     wort, StringComparer.OrdinalIgnoreCase)))
            .Where(profil => filter.Ort.Length == 0
                             || profil.Ort.Contains(filter.Ort, StringComparison.OrdinalIgnoreCase))
            .Where(profil => !filter.NurRemote || profil.RemoteMoeglich)
            .Take(seitenlaenge)
            .ToArray();

        return Task.FromResult(new Profilfundseite(treffer, null));
    }

    /// <inheritdoc />
    public Task<Profilfund?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new ProfilsucheSchweigt("profile-service antwortet im Test nicht");
        }

        return Task.FromResult(Bestand.FirstOrDefault(profil => profil.Wer == wer));
    }
}

/// <summary>Ein Ledger, den der Test steuert.</summary>
public sealed class Probetor : IEinwilligungstor
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
    public Task<IReadOnlyList<bool>> DarfSehenAlleAsync(
        IReadOnlyList<SubjectId> wer,
        TenantId firma,
        CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht");
        }

        if (AntwortetMitLaenge is { } falsch)
        {
            return Task.FromResult<IReadOnlyList<bool>>([.. Enumerable.Repeat(true, falsch)]);
        }

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. wer.Select(einer => Frei.Contains((einer.Value, firma.Value)))]);
    }
}

/// <summary>Belege, die der Test steuert.</summary>
public sealed class Probebelege : IBelege
{
    /// <summary>Was zu wem vorliegt.</summary>
    public Dictionary<Guid, Belegbogen> Bestand { get; } = [];

    /// <summary>Wen zuletzt gefragt wurde — in der Reihenfolge der Aufrufe.</summary>
    public List<Guid> Gefragt { get; } = [];

    /// <inheritdoc />
    public Task<Belegbogen> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        Gefragt.Add(wer.Value);

        return Task.FromResult(
            Bestand.TryGetValue(wer.Value, out var bogen)
                ? bogen
                : new Belegbogen([], Belegstand.KeineFreigegeben));
    }
}

/// <summary>Ein Entwerfer, der mitschreibt statt zu fragen.</summary>
public sealed class Probeentwerfer : IEntwerfer
{
    /// <summary>Was zuletzt hinausgegangen wäre — das ist die interessante Frage.</summary>
    public Ansprachelage? Letzte { get; private set; }

    /// <summary>Wenn wahr, ist kein Anbieter eingerichtet.</summary>
    public bool Fehlt { get; set; }

    /// <inheritdoc />
    public Task<string> EntwirfAsync(
        Ansprachelage lage, CancellationToken cancellationToken = default)
    {
        Letzte = lage;

        return Fehlt
            ? throw new AnspracheNichtVerfuegbar("im Test ist kein Anbieter eingerichtet")
            : Task.FromResult("Hallo, wir suchen jemanden für verteilte Systeme.");
    }
}
