using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Infrastructure.Persistence;

/// <summary>An entry as it lies in the jsonb column.</summary>
/// <remarks>
/// A shape of its own beside the value object, because the column is a
/// contract with stored rows: the value object may be renamed, the column may
/// not. On reading, everything runs through <c>Entry.From</c> — a row that no
/// longer satisfies the rules surfaces on its way out.
/// </remarks>
internal sealed record EntryRow(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("attachment")] string? Attachment,
    // Added later: older rows do not have the field, and `null` becomes an
    // empty list on reading. No follow-up migration is needed — at the next
    // save it stands there.
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>Reads and writes <c>portfolios</c>.</summary>
public sealed class EfPortfolioStore(PortfolioDbContext context, TimeProvider clock)
    : IPortfolioStore
{
    /// <inheritdoc />
    public async Task<Domain.Portfolios.Portfolio?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Portfolios
            .FirstOrDefaultAsync(candidate => candidate.Id == subject.Value, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        var entries = (JsonSerializer.Deserialize<List<EntryRow>>(row.Items) ?? [])
            .Select(read => Entry.From(
                read.Title, now, read.Summary, read.Url,
                read.Role, read.Year, read.Attachment, read.Technologies))
            .ToList();

        return Domain.Portfolios.Portfolio.Restore(
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

        var written = JsonSerializer.Serialize(
            portfolio.Entries.Select(entry => new EntryRow(
                entry.Title, entry.Summary, entry.Link,
                entry.Role, entry.Year, entry.Attachment, entry.Technologies)));

        var existing = await context.Portfolios
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == portfolio.Subject.Value, cancellationToken);

        if (existing is null)
        {
            context.Portfolios.Add(new PortfolioRow
            {
                Id = portfolio.Subject.Value,
                Items = written,
                CreatedAt = portfolio.CreatedAt.UtcDateTime,
                UpdatedAt = portfolio.ChangedAt.UtcDateTime
            });

            return;
        }

        existing.Items = written;
        existing.UpdatedAt = portfolio.ChangedAt.UtcDateTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Through the change tracker instead of <c>ExecuteDeleteAsync</c>: that
    /// would run immediately, and the row would be gone even if the command
    /// around it failed later — the files would then still be lying there.
    /// </remarks>
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Portfolios
            .AsTracking()
            .Where(candidate => candidate.Id == subject.Value)
            .ToListAsync(cancellationToken);

        context.Portfolios.RemoveRange(rows);

        // Nothing deliberately stays: a portfolio knows no retention switch.
        return 0;
    }
}
