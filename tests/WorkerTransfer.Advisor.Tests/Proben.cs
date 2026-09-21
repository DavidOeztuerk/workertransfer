using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Noelia.Core.Identity;
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

        await using var source = AdvisorDbContextFactory.DataSource(ConnectionString);
        await using var context = new AdvisorDbContext(
            (DbContextOptions<AdvisorDbContext>)AdvisorDbContextFactory.Configure(
                new DbContextOptionsBuilder<AdvisorDbContext>(), source).Options);

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

/// <summary>Ein Ledger, den der Test steuert.</summary>
/// <remarks>
/// Er hält fest, <em>was</em> geschrieben wurde — das ist die interessante
/// Frage: eine Stufe entsteht als Ledger-Ereignis und nirgends sonst.
/// </remarks>
public sealed class Probetor : IConsentGate
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
    public void Job(Guid subject, Guid company, Stufe stage)
    {
        var tenant = new TenantId(company);

        Loese(subject, StageCapabilities.Profile(tenant), stage >= Stufe.Profile);
        Loese(subject, StageCapabilities.Resume(tenant), stage >= Stufe.Unterlagen);
        Loese(subject, StageCapabilities.Klarname(tenant), stage >= Stufe.Person);
    }

    /// <summary>Setzt „für alle Unternehmen sichtbar".</summary>
    public void Oeffentlich(Guid subject, bool an) =>
        Loese(subject, StageCapabilities.ProfilOeffentlich, an);

    private void Loese(Guid subject, string skill, bool an)
    {
        var key = Schluessel(subject, skill);

        if (an)
        {
            Erteilt.Add(key);
        }
        else
        {
            Erteilt.Remove(key);
        }
    }

    /// <summary>Wer gerade handelt — im Test gesetzt, im Betrieb aus dem Token.</summary>
    public Guid Selbst { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<Stufe>> StagesAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new ConsentGateSilent("der Ledger antwortet im Test nicht");
        }

        return Task.FromResult<IReadOnlyList<Stufe>>([.. paare.Select(Lies)]);
    }

    private Stufe Lies((SubjectId Wer, TenantId Firma) paar)
    {
        var subject = paar.Wer.Value;

        var profile = Erteilt.Contains(Schluessel(subject, StageCapabilities.Profile(paar.Firma)))
                     || Erteilt.Contains(Schluessel(subject, StageCapabilities.ProfilOeffentlich));

        if (!profile)
        {
            return Stufe.None;
        }

        if (!Erteilt.Contains(Schluessel(subject, StageCapabilities.Resume(paar.Firma))))
        {
            return Stufe.Profile;
        }

        return Erteilt.Contains(Schluessel(subject, StageCapabilities.Klarname(paar.Firma)))
            ? Stufe.Person
            : Stufe.Unterlagen;
    }

    /// <inheritdoc />
    public Task GrantAsync(
        IReadOnlyList<string> skills, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new ConsentGateSilent("der Ledger antwortet im Test nicht");
        }

        if (LehntAb)
        {
            throw new ConsentRefused("a consent belongs to its subject");
        }

        foreach (var skill in skills)
        {
            Erteilungen.Add(skill);
            Erteilt.Add(Schluessel(Selbst, skill));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        IReadOnlyList<string> skills,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new ConsentGateSilent("der Ledger antwortet im Test nicht");
        }

        if (LehntAb)
        {
            throw new ConsentRefused("a consent belongs to its subject");
        }

        foreach (var skill in skills)
        {
            Widerrufe.Add(skill);
            Gruende.Add(reason);
            Erteilt.Remove(Schluessel(Selbst, skill));
        }

        return Task.CompletedTask;
    }

    private static string Schluessel(Guid subject, string skill) => $"{subject}|{skill}";
}

/// <summary>Eine Firmenauskunft, die der Test steuert.</summary>
public sealed class Probefirmen : ICompanyLookup
{
    /// <summary>Welches Unternehmen welche Domain hat.</summary>
    public Dictionary<Guid, string> Domains { get; } = [];

    /// <summary>Wie oft gefragt wurde — ohne Ausschluss soll gar nicht gefragt werden.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, antwortet identity-service gar nicht.</summary>
    public bool Schweigt { get; set; }

    /// <inheritdoc />
    public Task<CompanyLogo?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        Fragen++;

        if (Schweigt)
        {
            throw new LookupSilent("identity-service antwortet im Test nicht");
        }

        return Task.FromResult(
            Domains.TryGetValue(company.Value, out var domain)
                ? new CompanyLogo("Beispiel GmbH", domain)
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
    public Task<Personenbild?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        Gefragt.Add(subject.Value);

        return Task.FromResult(Bestand.GetValueOrDefault(subject.Value));
    }
}

/// <summary>Eine Übergabe, die der Test steuert.</summary>
public sealed class Probeuebergabe : ICaseHandover
{
    /// <summary>Für wen zuletzt übergeben wurde.</summary>
    public List<Guid> Uebergeben { get; } = [];

    /// <summary>Wenn wahr, lehnt transfer-service ab.</summary>
    public bool LehntAb { get; set; }

    /// <summary>Wenn wahr, antwortet transfer-service gar nicht.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Die Kennung, die zurückkommt.</summary>
    public Guid Case { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public Task<Guid> HandOverAsync(
        SubjectId subject, string anlass, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new LookupSilent("transfer-service antwortet im Test nicht");
        }

        if (LehntAb)
        {
            throw new HandoverRefused("transfer-service refused with 403");
        }

        Uebergeben.Add(subject.Value);

        return Task.FromResult(Case);
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
public sealed class Probegespraeche : IConversationStore
{
    private readonly List<Conversation> _bestand = [];

    /// <inheritdoc />
    public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.FirstOrDefault(g => g.Id == id));

    /// <inheritdoc />
    public Task<Conversation?> GetRunningAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.FirstOrDefault(
            g => g.Wer == subject && g.Firma == company && g.Laeuft));

    /// <inheritdoc />
    public Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        if (!_bestand.Contains(conversation))
        {
            _bestand.Add(conversation);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Conversation>>([.. _bestand.Where(g => g.Wer == subject)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Conversation>>([.. _bestand.Where(g => g.Firma == company)]);

    /// <inheritdoc />
    public Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        _bestand.RemoveAll(g => g.Wer == subject);

        return Task.FromResult(0);
    }
}

/// <summary>Mandate im Arbeitsspeicher.</summary>
public sealed class Probemandate : Domain.Mandates.IMandateStore
{
    private readonly Dictionary<Guid, Domain.Mandates.Mandate> _bestand = [];

    /// <inheritdoc />
    public Task<Domain.Mandates.Mandate?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bestand.GetValueOrDefault(subject.Value));

    /// <inheritdoc />
    public Task SaveAsync(
        Domain.Mandates.Mandate mandate, CancellationToken cancellationToken = default)
    {
        _bestand[mandate.Wer.Value] = mandate;

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
    public Task<Guid> NoteAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        Vermerke.Add((recipient.Value, kind));

        return Task.FromResult(Guid.NewGuid());
    }
}
