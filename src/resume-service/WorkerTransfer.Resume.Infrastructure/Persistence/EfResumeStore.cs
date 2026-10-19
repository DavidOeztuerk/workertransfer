using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Resumes;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>One position as it lies in the jsonb column.</summary>
/// <remarks>
/// Its own shape, deliberately not the boundary DTO and not the aggregate.
/// The wire may change without a data migration, and the aggregate may change
/// without breaking every stored row.
/// </remarks>
internal sealed record StoredPosition(
    [property: JsonPropertyName("employer")] string Employer,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string Description,
    // Added later: older rows do not have the field, and `null` becomes an
    // empty list on reading. No data migration is needed — it stands there at
    // the next save.
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>One stretch of education as it lies in the jsonb column.</summary>
internal sealed record StoredEducation(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("qualification")] string Qualification,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("kind")] string? Kind = null);

/// <summary>Reads and writes <c>resumes</c>.</summary>
/// <remarks>
/// Hands back a <em>detached</em> aggregate: the row is turned into a new
/// object, so a mutation reaches the database only through an explicit
/// <see cref="SaveAsync"/>. Forgetting it costs nothing in a test with a
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
        var row = await context.Resumes
            .SingleOrDefaultAsync(r => r.Id == subject.Value, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        CurriculumVitae resume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resume);

        var row = await context.Resumes
            .AsTracking()
            .SingleOrDefaultAsync(r => r.Id == resume.Subject.Value, cancellationToken);

        if (row is null)
        {
            context.Resumes.Add(new ResumeRow
            {
                Id = resume.Subject.Value,
                Positions = Write(resume.Positions),
                Education = Write(resume.Education),
                CreatedAt = resume.CreatedAt.UtcDateTime,
                UpdatedAt = resume.UpdatedAt.UtcDateTime
            });

            return;
        }

        row.Positions = Write(resume.Positions);
        row.Education = Write(resume.Education);
        row.Template = resume.Template;
        row.UpdatedAt = resume.UpdatedAt.UtcDateTime;
    }

    private static CurriculumVitae ToDomain(ResumeRow row)
    {
        var positions = JsonSerializer.Deserialize<List<StoredPosition>>(row.Positions, Format)
                        ?? [];
        var education = JsonSerializer
            .Deserialize<List<StoredEducation>>(row.Education, Format) ?? [];

        return CurriculumVitae.Restore(
            new SubjectId(row.Id),
            [.. positions.Select(p => Position.Create(
                p.Employer, p.Title, Month.Parse(p.StartedOn),
                p.EndedOn is null ? null : Month.Parse(p.EndedOn), p.Description,
                p.Technologies ?? []))],
            [.. education.Select(e => Education.Create(
                e.Institution, e.Qualification, Month.Parse(e.StartedOn),
                e.EndedOn is null ? null : Month.Parse(e.EndedOn),
                // The stored words, not the member names: rows written before
                // the translation hold exactly these.
                e.Kind == "schule" ? EducationKind.School : EducationKind.Vocational))],
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.Template);
    }

    private static string Write(IReadOnlyList<Position> positions) =>
        JsonSerializer.Serialize(
            positions.Select(p => new StoredPosition(
                p.Employer, p.Title, p.Start.ToString(), p.End?.ToString(), p.Description,
                p.Technologies)),
            Format);

    private static string Write(IReadOnlyList<Education> education) =>
        JsonSerializer.Serialize(
            education.Select(e => new StoredEducation(
                e.Institution, e.Qualification, e.Start.ToString(), e.End?.ToString(),
                e.Kind == EducationKind.School ? "schule" : "ausbildung")),
            Format);
}
