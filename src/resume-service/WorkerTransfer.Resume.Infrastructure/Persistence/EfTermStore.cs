using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Documents;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>A person's terms — names, never the wording.</summary>
/// <remarks>
/// <strong>Two questions and no third.</strong> "What stands in this human
/// being's documents" and "take away the terms for this file". A question about
/// the <em>word</em> — which human beings carry "Schweißfachmann" in their
/// documents — does not exist and must not exist: searchable is only what
/// somebody typed into their profile themselves (ADR-0033). That is also why the
/// word list lies as a field in the row and not as a table of its own in which
/// it could be searched.
/// </remarks>
public sealed class EfTermStore(ResumeDbContext context) : ITermStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentTerms>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Terms
            .AsNoTracking()
            .Where(row => row.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        DocumentTerms terms, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // `AsTracking()` IS THE WRITE. The whole context runs
        // `QueryTrackingBehavior.NoTracking`; without this line the row would
        // come back DETACHED, the assignments below would go nowhere, and the
        // second click on "read my documents" would silently write nothing —
        // measured on 09.09.2026 at another store in this service.
        var existing = await context.Terms
            .AsTracking()
            .FirstOrDefaultAsync(
                row => row.SubjectId == terms.Subject.Value
                    && row.DocumentId == terms.DocumentId,
                cancellationToken);

        if (existing is null)
        {
            existing = new TermRow { Id = terms.Id };
            context.Terms.Add(existing);
        }

        existing.SubjectId = terms.Subject.Value;
        existing.DocumentId = terms.DocumentId;
        existing.HasText = terms.TextFound;
        existing.Terms = JsonSerializer.Serialize(terms.Terms, Shape);
        existing.ReadAt = terms.ReadAt.UtcDateTime;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        SubjectId subject, Guid documentId, CancellationToken cancellationToken = default) =>
        context.Terms
            .Where(row => row.SubjectId == subject.Value && row.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>How the word list gets into the column.</summary>
    /// <remarks>
    /// Without <c>UnsafeRelaxedJsonEscaping</c> "C#" would be stored as
    /// <c>C#</c> — readable for the serialiser, unreadable for anybody who
    /// looks at the column. It is harmless here: none of it is ever delivered as
    /// HTML.
    /// </remarks>
    private static readonly JsonSerializerOptions Shape = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static DocumentTerms ToAggregate(TermRow row) =>
        DocumentTerms.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            row.DocumentId,
            row.HasText,
            JsonSerializer.Deserialize<string[]>(row.Terms) ?? [],
            new DateTimeOffset(row.ReadAt, TimeSpan.Zero));
}
