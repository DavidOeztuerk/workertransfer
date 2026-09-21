using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>One position as it lies in the jsonb column.</summary>
/// <remarks>
/// Its own shape, deliberately not the boundary DTO and not the aggregate.
/// The wire may change without a data migration, and the aggregate may change
/// without breaking every stored row.
/// </remarks>
internal sealed record StationsSatz(
    [property: JsonPropertyName("employer")] string Employer,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string Description,
    // Nachträglich dazugekommen: ältere Zeilen haben das Feld nicht, und `null`
    // wird beim Lesen zu einer leeren Liste. Eine Nachwanderung braucht es
    // nicht — beim nächsten Speichern steht es da.
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>One stretch of education as it lies in the jsonb column.</summary>
internal sealed record AusbildungsSatz(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("qualification")] string Qualification,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("kind")] string? Kind = null);

/// <summary>Reads and writes <c>resumes</c>.</summary>
/// <remarks>
/// Hands back a <em>detached</em> aggregate: the row is turned into a new
/// object, so a mutation reaches the database only through an explicit
/// <see cref="SichereAsync"/>. Forgetting it costs nothing in a test with a
/// fake that returns the same instance and silently loses the write in
/// production — which is why the repository is built so that it cannot.
/// </remarks>
public sealed class EfResumeStore(ResumeDbContext context) : IResumeStore
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<CurriculumVitae?> GetAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Lebenslaeufe
            .SingleOrDefaultAsync(z => z.Id == subject.Value, cancellationToken);

        return row is null ? null : ZuDomaene(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        CurriculumVitae resume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resume);

        var row = await context.Lebenslaeufe
            .AsTracking()
            .SingleOrDefaultAsync(z => z.Id == resume.Wer.Value, cancellationToken);

        if (row is null)
        {
            context.Lebenslaeufe.Add(new ResumeRow
            {
                Id = resume.Wer.Value,
                Positions = Write(resume.Stationen),
                Education = Write(resume.Ausbildungen),
                CreatedAt = resume.Angelegt.UtcDateTime,
                UpdatedAt = resume.Geaendert.UtcDateTime
            });

            return;
        }

        row.Positions = Write(resume.Stationen);
        row.Education = Write(resume.Ausbildungen);
        row.Template = resume.ResumeTemplate;
        row.UpdatedAt = resume.Geaendert.UtcDateTime;
    }

    private static CurriculumVitae ZuDomaene(ResumeRow row)
    {
        var stationen = JsonSerializer.Deserialize<List<StationsSatz>>(row.Positions, Format)
                        ?? [];
        var ausbildungen = JsonSerializer
            .Deserialize<List<AusbildungsSatz>>(row.Education, Format) ?? [];

        return CurriculumVitae.Wiederherstellen(
            new SubjectId(row.Id),
            [.. stationen.Select(s => Station.Aus(
                s.Employer, s.Title, Monat.Lies(s.StartedOn),
                s.EndedOn is null ? null : Monat.Lies(s.EndedOn), s.Description,
                s.Technologies ?? []))],
            [.. ausbildungen.Select(a => Ausbildung.Aus(
                a.Institution, a.Qualification, Monat.Lies(a.StartedOn),
                a.EndedOn is null ? null : Monat.Lies(a.EndedOn),
                a.Kind == "schule" ? Ausbildungsart.Schule : Ausbildungsart.Ausbildung))],
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.Template);
    }

    private static string Write(IReadOnlyList<Station> stationen) =>
        JsonSerializer.Serialize(
            stationen.Select(s => new StationsSatz(
                s.Arbeitgeber, s.Titel, s.Beginn.ToString(), s.Ende?.ToString(), s.Beschreibung,
                s.Technologien)),
            Format);

    private static string Write(IReadOnlyList<Ausbildung> ausbildungen) =>
        JsonSerializer.Serialize(
            ausbildungen.Select(a => new AusbildungsSatz(
                a.Einrichtung, a.Abschluss, a.Beginn.ToString(), a.Ende?.ToString(),
                a.Art == Ausbildungsart.Schule ? "schule" : "ausbildung")),
            Format);
}
