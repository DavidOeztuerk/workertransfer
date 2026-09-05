using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>applications</c>.</summary>
/// <remarks>
/// Gelesene Zeilen kommen als <em>neues</em> Aggregat zurück, nicht als
/// nachverfolgte Zeile. Eine Änderung erreicht die Datenbank deshalb nur über
/// <see cref="SichereAsync"/> — wer es vergisst, verliert den Schreibvorgang,
/// und das soll im Test genauso auffallen wie im Betrieb.
/// </remarks>
public sealed class EfBewerbungsspeicher(ApplicationsDbContext kontext) : IBewerbungsspeicher
{
    /// <inheritdoc />
    public async Task<Bewerbung?> HoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Bewerbungen
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task<Bewerbung?> HoleAsync(
        Guid stelle, SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Bewerbungen.FirstOrDefaultAsync(
            kandidat => kandidat.JobId == stelle && kandidat.SubjectId == wer.Value,
            cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Bewerbung bewerbung, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bewerbung);

        var vorhanden = await kontext.Bewerbungen
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == bewerbung.Id, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Bewerbungen.Add(ZurZeile(bewerbung));
            return;
        }

        Uebertrage(bewerbung, vorhanden);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bewerbung>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Bewerbungen
            .Where(kandidat => kandidat.SubjectId == wer.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bewerbung>> FuerStelleAsync(
        Guid stelle, TenantId firma, CancellationToken cancellationToken = default)
    {
        // Die Firma steht in der Abfrage, nicht in einem Filter danach: eine
        // fremde Stelle liefert eine leere Liste, statt zu verraten, dass es
        // sie gibt.
        var zeilen = await kontext.Bewerbungen
            .Where(kandidat => kandidat.JobId == stelle && kandidat.TenantId == firma.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Bewerbungsstand, int>> ZaehleAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var gezaehlt = await kontext.Bewerbungen
            .Where(kandidat => kandidat.TenantId == firma.Value)
            .GroupBy(kandidat => kandidat.Status)
            .Select(gruppe => new { Stand = gruppe.Key, Anzahl = gruppe.Count() })
            .ToListAsync(cancellationToken);

        var zahlen = new Dictionary<Bewerbungsstand, int>();

        foreach (var eintrag in gezaehlt)
        {
            // Ein Wort, das kein Stand ist, wird übergangen statt geworfen.
            // Diese Zahlen sind eine Bequemlichkeit; an ihnen soll keine Liste
            // scheitern, die das Unternehmen ohnehin einzeln sieht.
            if (Bewerbungsstaende.Lies(eintrag.Stand) is { } stand)
            {
                zahlen[stand] = eintrag.Anzahl;
            }
        }

        return zahlen;
    }

    private static Bewerbung ZumAggregat(BewerbungsZeile zeile) =>
        Bewerbung.Stelle_her(
            zeile.Id,
            zeile.JobId,
            new TenantId(zeile.TenantId),
            new SubjectId(zeile.SubjectId),
            zeile.Message,
            new Mitgeschicktes(
                zeile.SharesResume,
                zeile.SharesPortfolio,
                System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(zeile.Documents) ?? []),
            Bewerbungsstaende.Lies(zeile.Status)
            ?? throw new InvalidOperationException(
                $"Unbekannter Bewerbungsstand in der Datenbank: {zeile.Status}"),
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero),
            zeile.AnsweredAt is { } beantwortet
                ? new DateTimeOffset(beantwortet, TimeSpan.Zero)
                : null);

    private static BewerbungsZeile ZurZeile(Bewerbung bewerbung)
    {
        var zeile = new BewerbungsZeile
        {
            Id = bewerbung.Id,
            JobId = bewerbung.Stelle,
            TenantId = bewerbung.Firma.Value,
            SubjectId = bewerbung.Wer.Value,
            CreatedAt = bewerbung.AngelegtAm.UtcDateTime
        };

        Uebertrage(bewerbung, zeile);

        return zeile;
    }

    private static void Uebertrage(Bewerbung bewerbung, BewerbungsZeile zeile)
    {
        // Stelle, Firma, Person und der Zeitpunkt des Abschickens stehen hier
        // nicht: sie ändern sich nie, und eine Zuweisung, die es doch könnte,
        // wäre der Weg, eine Bewerbung nachträglich einem anderen zuzuschreiben.
        zeile.Message = bewerbung.Nachricht;
        zeile.SharesResume = bewerbung.Mitgeschickt.Lebenslauf;
        zeile.SharesPortfolio = bewerbung.Mitgeschickt.Portfolio;
        zeile.Documents = System.Text.Json.JsonSerializer.Serialize(
            bewerbung.Mitgeschickt.Unterlagen);
        zeile.Status = Bewerbungsstaende.Wort(bewerbung.Stand);
        zeile.UpdatedAt = bewerbung.GeaendertAm.UtcDateTime;
        zeile.AnsweredAt = bewerbung.BeantwortetAm?.UtcDateTime;
    }
}
