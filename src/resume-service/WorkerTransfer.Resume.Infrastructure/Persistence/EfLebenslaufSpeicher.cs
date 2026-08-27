using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
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
    [property: JsonPropertyName("description")] string Description);

/// <summary>One stretch of education as it lies in the jsonb column.</summary>
internal sealed record AusbildungsSatz(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("qualification")] string Qualification,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn);

/// <summary>Reads and writes <c>resumes</c>.</summary>
/// <remarks>
/// Hands back a <em>detached</em> aggregate: the row is turned into a new
/// object, so a mutation reaches the database only through an explicit
/// <see cref="SichereAsync"/>. Forgetting it costs nothing in a test with a
/// fake that returns the same instance and silently loses the write in
/// production — which is why the repository is built so that it cannot.
/// </remarks>
public sealed class EfLebenslaufSpeicher(ResumeDbContext kontext) : ILebenslaufSpeicher
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<Lebenslauf?> HoleAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Lebenslaeufe
            .SingleOrDefaultAsync(z => z.Id == wer.Value, cancellationToken);

        return zeile is null ? null : ZuDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Lebenslauf lebenslauf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lebenslauf);

        var zeile = await kontext.Lebenslaeufe
            .AsTracking()
            .SingleOrDefaultAsync(z => z.Id == lebenslauf.Wer.Value, cancellationToken);

        if (zeile is null)
        {
            kontext.Lebenslaeufe.Add(new LebenslaufZeile
            {
                Id = lebenslauf.Wer.Value,
                Positions = Schreibe(lebenslauf.Stationen),
                Education = Schreibe(lebenslauf.Ausbildungen),
                CreatedAt = lebenslauf.Angelegt.UtcDateTime,
                UpdatedAt = lebenslauf.Geaendert.UtcDateTime
            });

            return;
        }

        zeile.Positions = Schreibe(lebenslauf.Stationen);
        zeile.Education = Schreibe(lebenslauf.Ausbildungen);
        zeile.UpdatedAt = lebenslauf.Geaendert.UtcDateTime;
    }

    private static Lebenslauf ZuDomaene(LebenslaufZeile zeile)
    {
        var stationen = JsonSerializer.Deserialize<List<StationsSatz>>(zeile.Positions, Format)
                        ?? [];
        var ausbildungen = JsonSerializer
            .Deserialize<List<AusbildungsSatz>>(zeile.Education, Format) ?? [];

        return Lebenslauf.Wiederherstellen(
            new SubjectId(zeile.Id),
            [.. stationen.Select(s => Station.Aus(
                s.Employer, s.Title, Monat.Lies(s.StartedOn),
                s.EndedOn is null ? null : Monat.Lies(s.EndedOn), s.Description))],
            [.. ausbildungen.Select(a => Ausbildung.Aus(
                a.Institution, a.Qualification, Monat.Lies(a.StartedOn),
                a.EndedOn is null ? null : Monat.Lies(a.EndedOn)))],
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));
    }

    private static string Schreibe(IReadOnlyList<Station> stationen) =>
        JsonSerializer.Serialize(
            stationen.Select(s => new StationsSatz(
                s.Arbeitgeber, s.Titel, s.Beginn.ToString(), s.Ende?.ToString(), s.Beschreibung)),
            Format);

    private static string Schreibe(IReadOnlyList<Ausbildung> ausbildungen) =>
        JsonSerializer.Serialize(
            ausbildungen.Select(a => new AusbildungsSatz(
                a.Einrichtung, a.Abschluss, a.Beginn.ToString(), a.Ende?.ToString())),
            Format);
}
