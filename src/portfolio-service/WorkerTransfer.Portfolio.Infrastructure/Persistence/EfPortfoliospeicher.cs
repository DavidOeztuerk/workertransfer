using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Infrastructure.Persistence;

/// <summary>Ein Eintrag, wie er in der jsonb-Spalte liegt.</summary>
/// <remarks>
/// Eine eigene Form neben dem Wertobjekt, weil die Spalte ein Vertrag mit
/// gespeicherten Zeilen ist: das Wertobjekt darf umbenannt werden, die Spalte
/// nicht. Beim Lesen läuft alles durch <c>Eintrag.Aus</c> — eine Zeile, die die
/// Regeln nicht mehr erfüllt, fällt auf dem Weg nach draußen auf.
/// </remarks>
internal sealed record EntryRow(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("attachment")] string? Attachment,
    // Nachträglich dazugekommen: ältere Zeilen haben das Feld nicht, und `null`
    // wird beim Lesen zu einer leeren Liste. Eine Nachwanderung braucht es
    // nicht — beim nächsten Speichern steht es da.
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>Liest und schreibt <c>portfolios</c>.</summary>
public sealed class EfPortfolioStore(PortfolioDbContext context, TimeProvider uhr)
    : IPortfolioStore
{
    /// <inheritdoc />
    public async Task<Domain.Portfolios.Portfolio?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Portfolios
            .FirstOrDefaultAsync(kandidat => kandidat.Id == subject.Value, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var now = uhr.GetUtcNow();

        var entries = (JsonSerializer.Deserialize<List<EntryRow>>(row.Items) ?? [])
            .Select(gelesen => Eintrag.Aus(
                gelesen.Title, now, gelesen.Summary, gelesen.Url,
                gelesen.Role, gelesen.Year, gelesen.Attachment, gelesen.Technologies))
            .ToList();

        return Domain.Portfolios.Portfolio.Stelle_her(
            subject,
            entries,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Domain.Portfolios.Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        var geschrieben = JsonSerializer.Serialize(
            portfolio.Eintraege.Select(entry => new EntryRow(
                entry.Titel, entry.Zusammenfassung, entry.Link,
                entry.Rolle, entry.Jahr, entry.Anhang, entry.Technologien)));

        var vorhanden = await context.Portfolios
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == portfolio.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            context.Portfolios.Add(new PortfolioRow
            {
                Id = portfolio.Wer.Value,
                Items = geschrieben,
                CreatedAt = portfolio.AngelegtAm.UtcDateTime,
                UpdatedAt = portfolio.GeaendertAm.UtcDateTime
            });

            return;
        }

        vorhanden.Items = geschrieben;
        vorhanden.UpdatedAt = portfolio.GeaendertAm.UtcDateTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Über den Änderungsverfolger statt <c>ExecuteDeleteAsync</c>: das liefe
    /// sofort, und die Zeile wäre auch dann weg, wenn der Befehl darum herum
    /// später scheiterte — die Dateien lägen dann noch da.
    /// </remarks>
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Portfolios
            .AsTracking()
            .Where(kandidat => kandidat.Id == subject.Value)
            .ToListAsync(cancellationToken);

        context.Portfolios.RemoveRange(rows);

        // Nichts bleibt absichtlich stehen: ein Portfolio kennt keinen
        // Aufbewahrungsschalter.
        return 0;
    }
}
