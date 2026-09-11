using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Infrastructure.Persistence;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _behaelter = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("advisor")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql-Verbindungszeichenfolge zur gewanderten Datenbank.</summary>
    public string ConnectionString => _behaelter.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _behaelter.StartAsync();

        await using var quelle = AdvisorDbContextFactory.Datenquelle(ConnectionString);
        await using var kontext = new AdvisorDbContext(
            (DbContextOptions<AdvisorDbContext>)AdvisorDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<AdvisorDbContext>(), quelle).Options);

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

/// <summary>Ein Ledger, den der Test steuert.</summary>
/// <remarks>
/// Er hält fest, <em>was</em> geschrieben wurde — das ist die interessante
/// Frage: eine Stufe entsteht als Ledger-Ereignis und nirgends sonst.
/// </remarks>
public sealed class Probetor : IEinwilligungstor
{
    /// <summary>Welche Fähigkeit für wen gilt.</summary>
    public HashSet<string> Erteilt { get; } = [];

    /// <summary>Was zuletzt erteilt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<string> Erteilungen { get; } = [];

    /// <summary>Was zuletzt widerrufen wurde.</summary>
    public List<string> Widerrufe { get; } = [];

    /// <summary>Die Gründe, die dabei mitgingen.</summary>
    public List<string> Gruende { get; } = [];

    /// <summary>Wie oft nach Stufen gefragt wurde. Der Lesepfad muss jedes Mal fragen.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, sagt der Ledger gar nichts.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Wenn wahr, lehnt der Ledger jede Schreibung ab.</summary>
    public bool LehntAb { get; set; }

    /// <summary>Setzt eine Stufe, wie der echte Ledger sie hielte.</summary>
    public void Stelle(Guid wer, Guid firma, Stufe stufe)
    {
        var mandant = new TenantId(firma);

        Loese(wer, Stufenfaehigkeiten.Profil(mandant), stufe >= Stufe.Profil);
        Loese(wer, Stufenfaehigkeiten.Lebenslauf(mandant), stufe >= Stufe.Unterlagen);
        Loese(wer, Stufenfaehigkeiten.Klarname(mandant), stufe >= Stufe.Person);
    }

    /// <summary>Setzt „für alle Unternehmen sichtbar".</summary>
    public void Oeffentlich(Guid wer, bool an) =>
        Loese(wer, Stufenfaehigkeiten.ProfilOeffentlich, an);

    private void Loese(Guid wer, string faehigkeit, bool an)
    {
        var schluessel = Schluessel(wer, faehigkeit);

        if (an)
        {
            Erteilt.Add(schluessel);
        }
        else
        {
            Erteilt.Remove(schluessel);
        }
    }

    /// <summary>Wer gerade handelt — im Test gesetzt, im Betrieb aus dem Token.</summary>
    public Guid Selbst { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<Stufe>> StufenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht");
        }

        return Task.FromResult<IReadOnlyList<Stufe>>([.. paare.Select(Lies)]);
    }

    private Stufe Lies((SubjectId Wer, TenantId Firma) paar)
    {
        var wer = paar.Wer.Value;

        var profil = Erteilt.Contains(Schluessel(wer, Stufenfaehigkeiten.Profil(paar.Firma)))
                     || Erteilt.Contains(Schluessel(wer, Stufenfaehigkeiten.ProfilOeffentlich));

        if (!profil)
        {
            return Stufe.Keine;
        }

        if (!Erteilt.Contains(Schluessel(wer, Stufenfaehigkeiten.Lebenslauf(paar.Firma))))
        {
            return Stufe.Profil;
        }

        return Erteilt.Contains(Schluessel(wer, Stufenfaehigkeiten.Klarname(paar.Firma)))
            ? Stufe.Person
            : Stufe.Unterlagen;
    }

    /// <inheritdoc />
    public Task ErteileAsync(
        IReadOnlyList<string> faehigkeiten, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht");
        }

        if (LehntAb)
        {
            throw new EinwilligungAbgelehnt("a consent belongs to its subject");
        }

        foreach (var faehigkeit in faehigkeiten)
        {
            Erteilungen.Add(faehigkeit);
            Erteilt.Add(Schluessel(Selbst, faehigkeit));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WiderrufeAsync(
        IReadOnlyList<string> faehigkeiten,
        string grund,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht");
        }

        if (LehntAb)
        {
            throw new EinwilligungAbgelehnt("a consent belongs to its subject");
        }

        foreach (var faehigkeit in faehigkeiten)
        {
            Widerrufe.Add(faehigkeit);
            Gruende.Add(grund);
            Erteilt.Remove(Schluessel(Selbst, faehigkeit));
        }

        return Task.CompletedTask;
    }

    private static string Schluessel(Guid wer, string faehigkeit) => $"{wer}|{faehigkeit}";
}

/// <summary>Eine Firmenauskunft, die der Test steuert.</summary>
public sealed class Probefirmen : IFirmenauskunft
{
    /// <summary>Welches Unternehmen welche Domain hat.</summary>
    public Dictionary<Guid, string> Domains { get; } = [];

    /// <summary>Wie oft gefragt wurde — ohne Ausschluss soll gar nicht gefragt werden.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, antwortet identity-service gar nicht.</summary>
    public bool Schweigt { get; set; }

    /// <inheritdoc />
    public Task<Firmenbild?> HoleAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new AuskunftSchweigt("identity-service antwortet im Test nicht");
        }

        return Task.FromResult(
            Domains.TryGetValue(firma.Value, out var domain)
                ? new Firmenbild("Beispiel GmbH", domain)
                : null);
    }
}

/// <summary>Eine Personenauskunft, die der Test steuert.</summary>
public sealed class Probepersonen : IPersonenauskunft
{
    /// <summary>Wer wie heisst.</summary>
    public Dictionary<Guid, Personenbild> Bestand { get; } = [];

    /// <summary>Wen zuletzt gefragt wurde — in der Reihenfolge der Aufrufe.</summary>
    public List<Guid> Gefragt { get; } = [];

    /// <inheritdoc />
    public Task<Personenbild?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        Gefragt.Add(wer.Value);

        return Task.FromResult(Bestand.GetValueOrDefault(wer.Value));
    }
}

/// <summary>Eine Übergabe, die der Test steuert.</summary>
public sealed class Probeuebergabe : IVorgangsuebergabe
{
    /// <summary>Für wen zuletzt übergeben wurde.</summary>
    public List<Guid> Uebergeben { get; } = [];

    /// <summary>Wenn wahr, lehnt transfer-service ab.</summary>
    public bool LehntAb { get; set; }

    /// <summary>Wenn wahr, antwortet transfer-service gar nicht.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Die Kennung, die zurückkommt.</summary>
    public Guid Vorgang { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public Task<Guid> UebergibAsync(
        SubjectId wer, string anlass, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new AuskunftSchweigt("transfer-service antwortet im Test nicht");
        }

        if (LehntAb)
        {
            throw new UebergabeAbgelehnt("transfer-service refused with 403");
        }

        Uebergeben.Add(wer.Value);

        return Task.FromResult(Vorgang);
    }
}

/// <summary>Gespräche im Arbeitsspeicher.</summary>
/// <remarks>
/// Bewusst ein eigener Speicher statt einer Datenbank: die Handlerreihe fragt
/// nach <em>Entscheidungen</em>, nicht nach SQL. Dass die Zeilen wirklich
/// geschrieben werden, misst <c>BeraterreiseTests</c> gegen ein echtes
/// Postgres.
/// <para>
/// Er gibt dasselbe Aggregat zurück, das hineingelegt wurde. Das ist gutmütiger
/// als die Wirklichkeit — ein echtes Repository gibt losgelöste Aggregate
/// heraus — und genau deshalb steht die andere Reihe daneben.
/// </para>
/// </remarks>
public sealed class Probegespraeche : IGespraechsspeicher
{
    private readonly List<Gespraech> _bestand = [];

    /// <inheritdoc />
    public Task<Gespraech?> HoleAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.FirstOrDefault(g => g.Id == id));

    /// <inheritdoc />
    public Task<Gespraech?> HoleLaufendesAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.FirstOrDefault(
            g => g.Wer == wer && g.Firma == firma && g.Laeuft));

    /// <inheritdoc />
    public Task SichereAsync(Gespraech gespraech, CancellationToken cancellationToken = default)
    {
        if (!_bestand.Contains(gespraech))
        {
            _bestand.Add(gespraech);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Gespraech>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Gespraech>>([.. _bestand.Where(g => g.Wer == wer)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Gespraech>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Gespraech>>([.. _bestand.Where(g => g.Firma == firma)]);

    /// <inheritdoc />
    public Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default)
    {
        _bestand.RemoveAll(g => g.Wer == wer);

        return Task.FromResult(0);
    }
}

/// <summary>Mandate im Arbeitsspeicher.</summary>
public sealed class Probemandate : Domain.Mandate.IMandatspeicher
{
    private readonly Dictionary<Guid, Domain.Mandate.Mandat> _bestand = [];

    /// <inheritdoc />
    public Task<Domain.Mandate.Mandat?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.GetValueOrDefault(wer.Value));

    /// <inheritdoc />
    public Task SichereAsync(
        Domain.Mandate.Mandat mandat, CancellationToken cancellationToken = default)
    {
        _bestand[mandat.Wer.Value] = mandat;

        return Task.CompletedTask;
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
