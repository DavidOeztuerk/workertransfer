using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Documents;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>A person's documents, without their bytes.</summary>
/// <remarks>
/// As everywhere in this tree the aggregates come back <strong>detached</strong>:
/// a change reaches the database only through an explicit save. Forgetting it
/// costs nothing in the test and loses the write in production.
/// </remarks>
public sealed class EfDocumentRecordStore(ResumeDbContext context) : IDocumentRecordStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRecord>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Documents
            .AsNoTracking()
            .Where(row => row.SubjectId == subject.Value)
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<DocumentRecord?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entry => entry.Id == id && entry.SubjectId == subject.Value,
                cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRecord>> GetManyAsync(
        SubjectId subject, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await context.Documents
            .AsNoTracking()
            .Where(row => row.SubjectId == subject.Value && ids.Contains(row.Id))
            .ToListAsync(cancellationToken);

        // In the ORDER OF THE QUESTION, not in that of the database: the folder
        // shows the tabs the way the person enclosed them.
        var byId = rows.ToDictionary(row => row.Id);

        return
        [
            .. ids
                .Where(byId.ContainsKey)
                .Select(id => ToAggregate(byId[id]))
        ];
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        DocumentRecord document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // `AsTracking()` IS THE WRITE, not a subtlety.
        //
        // The whole context runs `QueryTrackingBehavior.NoTracking`
        // (`ResumeDbContextFactory`), and without this line the row comes back
        // DETACHED: the assignments below go nowhere, `SaveChanges` sees
        // nothing, and the caller still gets its 204. Measured on 09.09.2026 at
        // `PUT /resumes/me/documents/{id}/as-cv` — the answer was 204, the kind
        // stayed `sonstiges`.
        //
        // The insert path was untouched by it, because `Add` always tracks.
        // That is exactly why something like this surfaces only at the first
        // MUTATING caller, and this store had none until then. All eighteen
        // other stores in the tree call `AsTracking()`; this one was the only
        // one without.
        var existing = await context.Documents
            .AsTracking()
            .FirstOrDefaultAsync(row => row.Id == document.Id, cancellationToken);

        if (existing is null)
        {
            existing = new DocumentRecordRow { Id = document.Id };
            context.Documents.Add(existing);
        }

        existing.SubjectId = document.Subject.Value;
        existing.Name = document.Name;
        existing.Kind = document.Kind;
        existing.ContentType = document.ContentType;
        existing.SizeBytes = document.SizeBytes;
        existing.StorageKey = document.StorageKey;
        existing.CreatedAt = document.UploadedAt.UtcDateTime;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default) =>
        context.Documents
            .Where(row => row.Id == id && row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

    private static DocumentRecord ToAggregate(DocumentRecordRow row) =>
        DocumentRecord.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            row.Name,
            row.Kind,
            row.ContentType,
            row.SizeBytes,
            row.StorageKey,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero));
}
