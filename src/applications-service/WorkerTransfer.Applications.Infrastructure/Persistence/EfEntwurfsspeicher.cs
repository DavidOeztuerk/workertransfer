using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Die Entwürfe und ihre Anmerkungen.</summary>
/// <remarks>
/// Wie überall hier kommen die Aggregate <strong>abgelöst</strong> zurück: eine
/// Änderung erreicht die Datenbank nur über ein ausdrückliches Speichern.
/// </remarks>
public sealed class EfDraftStore(ApplicationsDbContext context) : IDraftStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationDraft>> MineAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Entwuerfe
            .Where(row => row.SubjectId == subject.Value)
            .OrderByDescending(row => row.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // EINE Abfrage für alle Anmerkungen, nicht eine je Entwurf: eine Liste
        // mit zwanzig Entwürfen kostete sonst einundzwanzig Fahrten.
        var kennungen = rows.Select(row => row.Id).ToList();
        var anmerkungen = await context.Anmerkungen
            .Where(row => kennungen.Contains(row.DraftId))
            .ToListAsync(cancellationToken);

        var nachEntwurf = anmerkungen
            .GroupBy(row => row.DraftId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return
        [
            .. rows.Select(row => ZumAggregat(
                row,
                nachEntwurf.TryGetValue(row.Id, out var eigene) ? eigene : []))
        ];
    }

    /// <inheritdoc />
    public async Task<ApplicationDraft?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Entwuerfe
            .FirstOrDefaultAsync(
                entry => entry.Id == id && entry.SubjectId == subject.Value,
                cancellationToken);

        if (row is null)
        {
            return null;
        }

        var anmerkungen = await context.Anmerkungen
            .Where(entry => entry.DraftId == id)
            .ToListAsync(cancellationToken);

        return ZumAggregat(row, anmerkungen);
    }

    /// <inheritdoc />
    public async Task<ApplicationDraft?> OpenOneAsync(
        SubjectId subject, Guid job, CancellationToken cancellationToken = default)
    {
        var row = await context.Entwuerfe
            .Where(entry => entry.SubjectId == subject.Value
                && entry.JobId == job
                && entry.Status != Standwort(DraftState.Gesendet))
            .OrderByDescending(entry => entry.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var anmerkungen = await context.Anmerkungen
            .Where(entry => entry.DraftId == row.Id)
            .ToListAsync(cancellationToken);

        return ZumAggregat(row, anmerkungen);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ApplicationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var row = await context.Entwuerfe
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == draft.Id, cancellationToken);

        if (row is null)
        {
            row = new DraftRow { Id = draft.Id, CreatedAt = draft.Angelegt.UtcDateTime };
            context.Entwuerfe.Add(row);
        }

        row.JobId = draft.Job;
        row.TenantId = draft.Firma.Value;
        row.SubjectId = draft.Wer.Value;
        row.Subject = draft.Betreff;
        row.Body = draft.Text;
        row.Status = Standwort(draft.State);
        row.Version = draft.Fassung;
        row.Error = draft.Fehler;
        row.SharesResume = draft.TeiltLebenslauf;
        row.Documents = JsonSerializer.Serialize(draft.Unterlagen);
        row.UpdatedAt = draft.Geaendert.UtcDateTime;
        row.WritingStartedAt = draft.SchreibenBegonnen?.UtcDateTime;

        // Die Anmerkungen: neue anlegen, abgehakte nachziehen. Gelöscht wird
        // keine — eine Anmerkung ist der Evidence dafür, dass jemand widersprochen
        // hat, und sie verschwinden zu lassen hiesse, die Fassungsgeschichte
        // um ihre Begründung zu bringen.
        var vorhandene = await context.Anmerkungen
            .AsTracking()
            .Where(entry => entry.DraftId == draft.Id)
            .ToListAsync(cancellationToken);

        foreach (var anmerkung in draft.Anmerkungen)
        {
            var found = vorhandene.FirstOrDefault(entry => entry.Id == anmerkung.Id);

            if (found is null)
            {
                context.Anmerkungen.Add(new CommentRow
                {
                    Id = anmerkung.Id,
                    DraftId = draft.Id,
                    Body = anmerkung.Text,
                    Quote = anmerkung.Zitat,
                    Resolved = anmerkung.Erledigt,
                    CreatedAt = anmerkung.Angelegt.UtcDateTime
                });

                continue;
            }

            found.Resolved = anmerkung.Erledigt;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default)
    {
        await context.Anmerkungen
            .Where(row => row.DraftId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Entwuerfe
            .Where(row => row.Id == id && row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Der State als Wort — snake_case, wie überall auf der Leitung.</summary>
    public static string Standwort(DraftState state) => state switch
    {
        DraftState.Entsteht => "generating",
        DraftState.Pruefen => "review",
        DraftState.Ueberarbeiten => "needs_changes",
        DraftState.Freigegeben => "approved",
        DraftState.Gesendet => "sent",
        _ => "failed"
    };

    /// <summary>Das Wort als State.</summary>
    public static DraftState Standwahl(string? word) => word switch
    {
        "review" => DraftState.Pruefen,
        "needs_changes" => DraftState.Ueberarbeiten,
        "approved" => DraftState.Freigegeben,
        "sent" => DraftState.Gesendet,
        "failed" => DraftState.Fehlgeschlagen,
        _ => DraftState.Entsteht
    };

    private static ApplicationDraft ZumAggregat(
        DraftRow row, IReadOnlyList<CommentRow> anmerkungen) =>
        ApplicationDraft.Stelle_her(
            row.Id,
            row.JobId,
            new TenantId(row.TenantId),
            new SubjectId(row.SubjectId),
            row.Subject,
            row.Body,
            Standwahl(row.Status),
            row.Version,
            row.Error,
            row.SharesResume,
            JsonSerializer.Deserialize<List<Guid>>(row.Documents) ?? [],
            [
                .. anmerkungen.Select(entry => Anmerkung.Stelle_her(
                    entry.Id, entry.Body, entry.Quote, entry.Resolved,
                    new DateTimeOffset(entry.CreatedAt, TimeSpan.Zero)))
            ],
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.WritingStartedAt is { } start
                ? new DateTimeOffset(start, TimeSpan.Zero)
                : null);
}
