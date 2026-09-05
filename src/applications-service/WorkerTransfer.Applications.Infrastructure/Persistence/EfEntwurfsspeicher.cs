using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Die Entwürfe und ihre Anmerkungen.</summary>
/// <remarks>
/// Wie überall hier kommen die Aggregate <strong>abgelöst</strong> zurück: eine
/// Änderung erreicht die Datenbank nur über ein ausdrückliches Speichern.
/// </remarks>
public sealed class EfEntwurfsspeicher(ApplicationsDbContext kontext) : IEntwurfsspeicher
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Bewerbungsentwurf>> MeineAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Entwuerfe
            .Where(zeile => zeile.SubjectId == wer.Value)
            .OrderByDescending(zeile => zeile.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (zeilen.Count == 0)
        {
            return [];
        }

        // EINE Abfrage für alle Anmerkungen, nicht eine je Entwurf: eine Liste
        // mit zwanzig Entwürfen kostete sonst einundzwanzig Fahrten.
        var kennungen = zeilen.Select(zeile => zeile.Id).ToList();
        var anmerkungen = await kontext.Anmerkungen
            .Where(zeile => kennungen.Contains(zeile.DraftId))
            .ToListAsync(cancellationToken);

        var nachEntwurf = anmerkungen
            .GroupBy(zeile => zeile.DraftId)
            .ToDictionary(gruppe => gruppe.Key, gruppe => gruppe.ToList());

        return
        [
            .. zeilen.Select(zeile => ZumAggregat(
                zeile,
                nachEntwurf.TryGetValue(zeile.Id, out var eigene) ? eigene : []))
        ];
    }

    /// <inheritdoc />
    public async Task<Bewerbungsentwurf?> HoleAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Entwuerfe
            .FirstOrDefaultAsync(
                eintrag => eintrag.Id == id && eintrag.SubjectId == wer.Value,
                cancellationToken);

        if (zeile is null)
        {
            return null;
        }

        var anmerkungen = await kontext.Anmerkungen
            .Where(eintrag => eintrag.DraftId == id)
            .ToListAsync(cancellationToken);

        return ZumAggregat(zeile, anmerkungen);
    }

    /// <inheritdoc />
    public async Task<Bewerbungsentwurf?> OffenerAsync(
        SubjectId wer, Guid stelle, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Entwuerfe
            .Where(eintrag => eintrag.SubjectId == wer.Value
                && eintrag.JobId == stelle
                && eintrag.Status != Standwort(Entwurfsstand.Gesendet))
            .OrderByDescending(eintrag => eintrag.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (zeile is null)
        {
            return null;
        }

        var anmerkungen = await kontext.Anmerkungen
            .Where(eintrag => eintrag.DraftId == zeile.Id)
            .ToListAsync(cancellationToken);

        return ZumAggregat(zeile, anmerkungen);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Bewerbungsentwurf entwurf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entwurf);

        var zeile = await kontext.Entwuerfe
            .FirstOrDefaultAsync(eintrag => eintrag.Id == entwurf.Id, cancellationToken);

        if (zeile is null)
        {
            zeile = new EntwurfsZeile { Id = entwurf.Id, CreatedAt = entwurf.Angelegt.UtcDateTime };
            kontext.Entwuerfe.Add(zeile);
        }

        zeile.JobId = entwurf.Stelle;
        zeile.TenantId = entwurf.Firma.Value;
        zeile.SubjectId = entwurf.Wer.Value;
        zeile.Subject = entwurf.Betreff;
        zeile.Body = entwurf.Text;
        zeile.Status = Standwort(entwurf.Stand);
        zeile.Version = entwurf.Fassung;
        zeile.Error = entwurf.Fehler;
        zeile.SharesResume = entwurf.TeiltLebenslauf;
        zeile.Documents = JsonSerializer.Serialize(entwurf.Unterlagen);
        zeile.UpdatedAt = entwurf.Geaendert.UtcDateTime;

        // Die Anmerkungen: neue anlegen, abgehakte nachziehen. Gelöscht wird
        // keine — eine Anmerkung ist der Beleg dafür, dass jemand widersprochen
        // hat, und sie verschwinden zu lassen hiesse, die Fassungsgeschichte
        // um ihre Begründung zu bringen.
        var vorhandene = await kontext.Anmerkungen
            .Where(eintrag => eintrag.DraftId == entwurf.Id)
            .ToListAsync(cancellationToken);

        foreach (var anmerkung in entwurf.Anmerkungen)
        {
            var gefunden = vorhandene.FirstOrDefault(eintrag => eintrag.Id == anmerkung.Id);

            if (gefunden is null)
            {
                kontext.Anmerkungen.Add(new AnmerkungsZeile
                {
                    Id = anmerkung.Id,
                    DraftId = entwurf.Id,
                    Body = anmerkung.Text,
                    Quote = anmerkung.Zitat,
                    Resolved = anmerkung.Erledigt,
                    CreatedAt = anmerkung.Angelegt.UtcDateTime
                });

                continue;
            }

            gefunden.Resolved = anmerkung.Erledigt;
        }
    }

    /// <inheritdoc />
    public async Task LoescheAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default)
    {
        await kontext.Anmerkungen
            .Where(zeile => zeile.DraftId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await kontext.Entwuerfe
            .Where(zeile => zeile.Id == id && zeile.SubjectId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Der Stand als Wort — snake_case, wie überall auf der Leitung.</summary>
    public static string Standwort(Entwurfsstand stand) => stand switch
    {
        Entwurfsstand.Entsteht => "generating",
        Entwurfsstand.Pruefen => "review",
        Entwurfsstand.Ueberarbeiten => "needs_changes",
        Entwurfsstand.Freigegeben => "approved",
        Entwurfsstand.Gesendet => "sent",
        _ => "failed"
    };

    /// <summary>Das Wort als Stand.</summary>
    public static Entwurfsstand Standwahl(string? wort) => wort switch
    {
        "review" => Entwurfsstand.Pruefen,
        "needs_changes" => Entwurfsstand.Ueberarbeiten,
        "approved" => Entwurfsstand.Freigegeben,
        "sent" => Entwurfsstand.Gesendet,
        "failed" => Entwurfsstand.Fehlgeschlagen,
        _ => Entwurfsstand.Entsteht
    };

    private static Bewerbungsentwurf ZumAggregat(
        EntwurfsZeile zeile, IReadOnlyList<AnmerkungsZeile> anmerkungen) =>
        Bewerbungsentwurf.Stelle_her(
            zeile.Id,
            zeile.JobId,
            new TenantId(zeile.TenantId),
            new SubjectId(zeile.SubjectId),
            zeile.Subject,
            zeile.Body,
            Standwahl(zeile.Status),
            zeile.Version,
            zeile.Error,
            zeile.SharesResume,
            JsonSerializer.Deserialize<List<Guid>>(zeile.Documents) ?? [],
            [
                .. anmerkungen.Select(eintrag => Anmerkung.Stelle_her(
                    eintrag.Id, eintrag.Body, eintrag.Quote, eintrag.Resolved,
                    new DateTimeOffset(eintrag.CreatedAt, TimeSpan.Zero)))
            ],
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));
}
