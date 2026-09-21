using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>applications</c>.</summary>
/// <remarks>
/// Gelesene Zeilen kommen als <em>neues</em> Aggregat zurück, nicht als
/// nachverfolgte Zeile. Eine Änderung erreicht die Datenbank deshalb nur über
/// <see cref="SichereAsync"/> — wer es vergisst, verliert den Schreibvorgang,
/// und das soll im Test genauso auffallen wie im Betrieb.
/// </remarks>
public sealed class EfApplicationStore(ApplicationsDbContext context) : IApplicationStore
{
    /// <inheritdoc />
    public async Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Applications
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<JobApplication?> GetAsync(
        Guid job, SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Applications.FirstOrDefaultAsync(
            kandidat => kandidat.JobId == job && kandidat.SubjectId == subject.Value,
            cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        JobApplication application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);

        var vorhanden = await context.Applications
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == application.Id, cancellationToken);

        if (vorhanden is null)
        {
            context.Applications.Add(ZurZeile(application));
            return;
        }

        Uebertrage(application, vorhanden);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobApplication>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Applications
            .Where(kandidat => kandidat.SubjectId == subject.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobApplication>> ForJobAsync(
        Guid job, TenantId company, CancellationToken cancellationToken = default)
    {
        // Die Firma steht in der Abfrage, nicht in einem Filter danach: eine
        // fremde Job liefert eine leere Liste, statt zu verraten, dass es
        // sie gibt.
        var rows = await context.Applications
            .Where(kandidat => kandidat.JobId == job && kandidat.TenantId == company.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ApplicationState, int>> CountAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var gezaehlt = await context.Applications
            .Where(kandidat => kandidat.TenantId == company.Value)
            .GroupBy(kandidat => kandidat.Status)
            .Select(group => new { State = group.Key, Anzahl = group.Count() })
            .ToListAsync(cancellationToken);

        var zahlen = new Dictionary<ApplicationState, int>();

        foreach (var entry in gezaehlt)
        {
            // Ein Wort, das kein State ist, wird übergangen statt geworfen.
            // Diese Zahlen sind eine Bequemlichkeit; an ihnen soll keine Liste
            // scheitern, die das Unternehmen ohnehin einzeln sieht.
            if (ApplicationStates.Lies(entry.State) is { } state)
            {
                zahlen[state] = entry.Anzahl;
            }
        }

        return zahlen;
    }

    private static JobApplication ZumAggregat(ApplicationRow row) =>
        JobApplication.Stelle_her(
            row.Id,
            row.JobId,
            new TenantId(row.TenantId),
            new SubjectId(row.SubjectId),
            row.Message,
            new Mitgeschicktes(
                row.SharesResume,
                row.SharesPortfolio,
                System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(row.Documents) ?? []),
            ApplicationStates.Lies(row.Status)
            ?? throw new InvalidOperationException(
                $"Unbekannter Bewerbungsstand in der Datenbank: {row.Status}"),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.AnsweredAt is { } beantwortet
                ? new DateTimeOffset(beantwortet, TimeSpan.Zero)
                : null,
            LiesKontakt(row.ApplicantContact));

    private static ApplicationRow ZurZeile(JobApplication application)
    {
        var row = new ApplicationRow
        {
            Id = application.Id,
            JobId = application.Job,
            TenantId = application.Firma.Value,
            SubjectId = application.Wer.Value,
            CreatedAt = application.AngelegtAm.UtcDateTime
        };

        Uebertrage(application, row);

        return row;
    }

    private static void Uebertrage(JobApplication application, ApplicationRow row)
    {
        // Job, Firma, Person und der Zeitpunkt des Abschickens stehen hier
        // nicht: sie ändern sich nie, und eine Zuweisung, die es doch könnte,
        // wäre der Weg, eine JobApplication nachträglich einem anderen zuzuschreiben.
        row.Message = application.Nachricht;
        row.SharesResume = application.Mitgeschickt.CurriculumVitae;
        row.SharesPortfolio = application.Mitgeschickt.Portfolio;
        row.Documents = System.Text.Json.JsonSerializer.Serialize(
            application.Mitgeschickt.Unterlagen);
        row.Status = ApplicationStates.Wort(application.State);
        row.UpdatedAt = application.GeaendertAm.UtcDateTime;
        row.AnsweredAt = application.BeantwortetAm?.UtcDateTime;
        row.ApplicantContact = System.Text.Json.JsonSerializer.Serialize(
            new KontaktDraht(application.Kontakt));
    }

    private static ApplicantContact LiesKontakt(string roh)
    {
        if (string.IsNullOrWhiteSpace(roh) || roh == "{}")
        {
            return ApplicantContact.Leer;
        }

        try
        {
            var draht = System.Text.Json.JsonSerializer.Deserialize<KontaktDraht>(roh);
            return draht is null
                ? ApplicantContact.Leer
                : new ApplicantContact(
                    draht.Klarname ?? string.Empty,
                    draht.Zeile1 ?? string.Empty,
                    draht.Zeile2 ?? string.Empty,
                    draht.Postleitzahl ?? string.Empty,
                    draht.Ort ?? string.Empty,
                    string.IsNullOrEmpty(draht.Land) ? "DE" : draht.Land,
                    draht.Telefon ?? string.Empty,
                    draht.Email ?? string.Empty);
        }
        catch (System.Text.Json.JsonException)
        {
            return ApplicantContact.Leer;
        }
    }

    private sealed record KontaktDraht(
        [property: System.Text.Json.Serialization.JsonPropertyName("full_name")] string? Klarname = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("line1")] string? Zeile1 = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("line2")] string? Zeile2 = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("postal_code")] string? Postleitzahl = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("city")] string? Ort = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("country")] string? Land = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("phone")] string? Telefon = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("email")] string? Email = null)
    {
        public KontaktDraht() : this(null, null, null, null, null, null, null, null)
        {
        }

        public KontaktDraht(ApplicantContact kontakt)
            : this(
                kontakt.Klarname,
                kontakt.Zeile1,
                kontakt.Zeile2,
                kontakt.Postleitzahl,
                kontakt.Ort,
                kontakt.Land,
                kontakt.Telefon,
                kontakt.Email)
        {
        }
    }
}
