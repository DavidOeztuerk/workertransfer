using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Anfragen;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Reads and writes <c>resume_requests</c>.</summary>
public sealed class EfAnfragenSpeicher(ResumeDbContext kontext) : IAnfragenSpeicher
{
    /// <inheritdoc />
    public async Task<Anfrage?> HoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Anfragen.SingleOrDefaultAsync(z => z.Id == id, cancellationToken);

        return zeile is null ? null : ZuDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task<Anfrage?> FindeAsync(
        SubjectId wer,
        TenantId firma,
        CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Anfragen.SingleOrDefaultAsync(
            z => z.SubjectId == wer.Value && z.TenantId == firma.Value, cancellationToken);

        return zeile is null ? null : ZuDomaene(zeile);
    }

    /// <inheritdoc />
    public Task FuegeHinzuAsync(Anfrage anfrage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anfrage);

        kontext.Anfragen.Add(new AnfrageZeile
        {
            Id = anfrage.Id,
            SubjectId = anfrage.Wer.Value,
            TenantId = anfrage.Firma.Value,
            RequestedBy = anfrage.Frager?.Value,
            Status = anfrage.Stand,
            CreatedAt = anfrage.Gestellt.UtcDateTime,
            AnsweredAt = anfrage.Beantwortet?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SichereAsync(Anfrage anfrage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anfrage);

        var zeile = await kontext.Anfragen
            .AsTracking()
            .SingleOrDefaultAsync(z => z.Id == anfrage.Id, cancellationToken);

        if (zeile is null)
        {
            return;
        }

        // Only the two an answer may move. `requested_by` is not among them:
        // it is nulled by the erasure alone, and re-writing it here would put a
        // deleted person's id back (ADR-0027 §2).
        zeile.Status = anfrage.Stand;
        zeile.AnsweredAt = anfrage.Beantwortet?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Anfrage>> FuerPersonAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default) =>
        [.. (await kontext.Anfragen
                .Where(z => z.SubjectId == wer.Value)
                .OrderByDescending(z => z.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ZuDomaene)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Anfrage>> FuerFirmaAsync(
        TenantId firma,
        CancellationToken cancellationToken = default) =>
        [.. (await kontext.Anfragen
                .Where(z => z.TenantId == firma.Value)
                .OrderByDescending(z => z.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ZuDomaene)];

    private static Anfrage ZuDomaene(AnfrageZeile zeile) =>
        Anfrage.Wiederherstellen(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            new TenantId(zeile.TenantId),
            zeile.RequestedBy is { } frager ? new SubjectId(frager) : null,
            zeile.Status,
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            zeile.AnsweredAt is { } beantwortet
                ? new DateTimeOffset(beantwortet, TimeSpan.Zero)
                : null);
}
