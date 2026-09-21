using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>Die gespeicherten Searches in Postgres.</summary>
public sealed class EfSearchStore(ScoutDbContext context) : ISearchStore
{
    /// <inheritdoc />
    public Task AppendAsync(Search search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);

        context.Searches.Add(new SearchRow
        {
            Id = search.Id,
            TenantId = search.Firma.Value,
            SubjectId = search.Wer.Value,
            Name = search.Name,
            Skills = [.. search.Filter.GenannteWorte],
            Location = search.Filter.Ort,
            Remote = search.Filter.NurRemote,
            CreatedAt = search.AngelegtAm.UtcDateTime
        });

        // Ohne SaveChanges: der Befehl läuft in der Transaktionsklammer, und
        // was dort nicht gemeinsam festgeschrieben wird, ist einzeln
        // festgeschrieben — das ist etwas anderes.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Search>> ForHumanAsync(
        TenantId company, SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Searches
            .Where(row => row.TenantId == company.Value && row.SubjectId == subject.Value)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZurDomaene)];
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        Guid id, TenantId company, SubjectId subject, CancellationToken cancellationToken = default)
    {
        // AsTracking, weil hier geändert wird: der Kontext liest sonst
        // unverfolgt, `Remove` bekäme eine losgelöste Zeile, und der Aufrufer
        // sähe trotzdem ein „erledigt".
        var row = await context.Searches
            .AsTracking()
            .FirstOrDefaultAsync(
                entry => entry.Id == id
                           && entry.TenantId == company.Value
                           && entry.SubjectId == subject.Value,
                cancellationToken);

        if (row is null)
        {
            // „Gehört dir nicht" und „gibt es nicht" antworten gleich: ein
            // Unterschied verriete, dass die Search eines Kollegen existiert.
            return false;
        }

        context.Searches.Remove(row);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var searches = await context.Searches
            .AsTracking()
            .Where(row => row.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Searches.RemoveRange(searches);

        // Und die Vermerke ÜBER diesen Menschen. Sie tragen seine Kennung und
        // sind damit personenbezogen — eine Löschung, die nur die eigenen
        // Searches wegnimmt, liesse in jeder Sicherung stehen, dass es diese
        // Person gab (ADR-0027).
        var vermerke = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(row => row.UserId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(vermerke);

        // Immer 0. Dieser Dienst kennt keinen Aufbewahrungsfall: hier steht
        // nichts, was jemand anderem gehört.
        return 0;
    }

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über <see cref="Suche.Stelle_her"/> und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hiesse, jemandem seine Search zu entziehen, weil sich eine
    /// Obergrenze geändert hat. Kanonisiert wird trotzdem — ein neuer Eintrag
    /// im Vocabulary wirkt so auch auf ältere Zeilen, ohne Datenwanderung.
    /// </remarks>
    private static Search ZurDomaene(SearchRow row) =>
        Search.Stelle_her(
            row.Id,
            new TenantId(row.TenantId),
            new SubjectId(row.SubjectId),
            row.Name,
            SearchFilter.Stelle_her(row.Skills, row.Location, row.Remote),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero));
}
