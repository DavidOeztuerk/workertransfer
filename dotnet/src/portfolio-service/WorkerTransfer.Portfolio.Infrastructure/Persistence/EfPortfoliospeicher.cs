using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
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
internal sealed record EintragZeile(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("attachment")] string? Attachment);

/// <summary>Liest und schreibt <c>portfolios</c>.</summary>
public sealed class EfPortfoliospeicher(PortfolioDbContext kontext, TimeProvider uhr)
    : IPortfoliospeicher
{
    /// <inheritdoc />
    public async Task<Domain.Portfolios.Portfolio?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Portfolios
            .FirstOrDefaultAsync(kandidat => kandidat.Id == wer.Value, cancellationToken);

        if (zeile is null)
        {
            return null;
        }

        var jetzt = uhr.GetUtcNow();

        var eintraege = (JsonSerializer.Deserialize<List<EintragZeile>>(zeile.Items) ?? [])
            .Select(gelesen => Eintrag.Aus(
                gelesen.Title, jetzt, gelesen.Summary, gelesen.Url,
                gelesen.Role, gelesen.Year, gelesen.Attachment))
            .ToList();

        return Domain.Portfolios.Portfolio.Stelle_her(
            wer,
            eintraege,
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Domain.Portfolios.Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        var geschrieben = JsonSerializer.Serialize(
            portfolio.Eintraege.Select(eintrag => new EintragZeile(
                eintrag.Titel, eintrag.Zusammenfassung, eintrag.Link,
                eintrag.Rolle, eintrag.Jahr, eintrag.Anhang)));

        var vorhanden = await kontext.Portfolios
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == portfolio.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Portfolios.Add(new PortfolioZeile
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
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Portfolios
            .AsTracking()
            .Where(kandidat => kandidat.Id == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Portfolios.RemoveRange(zeilen);

        // Nichts bleibt absichtlich stehen: ein Portfolio kennt keinen
        // Aufbewahrungsschalter.
        return 0;
    }
}
