using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Jobs.Domain.Stellen;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>jobs</c>.</summary>
public sealed class EfStellenspeicher(JobsDbContext kontext) : IStellenspeicher
{
    /// <inheritdoc />
    public async Task<Stelle?> HoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Stellen
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(Stelle stelle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stelle);

        var vorhanden = await kontext.Stellen
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == stelle.Id, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Stellen.Add(ZurZeile(stelle));
            return;
        }

        Uebertrage(stelle, vorhanden);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Stelle>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Stellen
            .Where(kandidat => kandidat.TenantId == firma.Value)
            .OrderByDescending(kandidat => kandidat.UpdatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<Stellenseite> SucheAsync(
        int anzahl,
        string? zeiger,
        IReadOnlyList<string>? faehigkeiten,
        string ort,
        Remotegrad? remote,
        CancellationToken cancellationToken = default)
    {
        // Der Stand wird HIER gefiltert und nie vom Aufrufer: ein
        // `status=draft` auf der Leitung wäre ein Weg, fremde Entwürfe zu
        // lesen.
        var abfrage = kontext.Stellen.Where(kandidat => kandidat.Status == "published");

        if (!string.IsNullOrWhiteSpace(ort))
        {
            abfrage = abfrage.Where(kandidat => EF.Functions.ILike(kandidat.Location, $"%{ort}%"));
        }

        if (remote is { } grad)
        {
            var gesucht = Stand(grad);
            abfrage = abfrage.Where(kandidat => kandidat.RemoteMode == gesucht);
        }

        // Der Zeiger ist der Zeitpunkt der Veröffentlichung plus die Id: zwei
        // Anzeigen in derselben Millisekunde bekommen sonst je nach Laune der
        // Datenbank eine andere Reihenfolge, und eine Seite überspringt eine.
        if (Zeiger.Lies(zeiger) is { } weiter)
        {
            abfrage = abfrage.Where(kandidat =>
                kandidat.PublishedAt < weiter.Am
                || (kandidat.PublishedAt == weiter.Am && kandidat.Id < weiter.Id));
        }

        var zeilen = await abfrage
            .OrderByDescending(kandidat => kandidat.PublishedAt)
            .ThenByDescending(kandidat => kandidat.Id)
            .Take(anzahl + 1)
            .ToListAsync(cancellationToken);

        var mehr = zeilen.Count > anzahl;
        var seite = zeilen.Take(anzahl).Select(ZumAggregat).ToList();

        // Nach Fähigkeiten wird im Speicher gefiltert, nicht in SQL. Der Grund
        // ist der Wortschatz: „Postgres" in der Anfrage muss „PostgreSQL" in
        // der Anzeige treffen, und die Kanonisierung steht im Code, nicht in
        // der Datenbank. Bei einer Seite von zwanzig ist das billig; würde es
        // teuer, gehörte die kanonische Form in eine eigene Spalte — und nicht
        // die Regel in SQL nachgebaut.
        if (faehigkeiten is { Count: > 0 })
        {
            var gesucht = Faehigkeitenliste.Aus(faehigkeiten).Werte;

            seite = [.. seite.Where(stelle => gesucht.All(einzeln =>
                stelle.Faehigkeiten.Werte.Contains(einzeln, StringComparer.OrdinalIgnoreCase)))];
        }

        var letzte = mehr ? zeilen[anzahl - 1] : null;

        return new Stellenseite(
            seite,
            letzte?.PublishedAt is { } am ? new Zeiger(am, letzte.Id).ToString() : null);
    }

    /// <inheritdoc />
    public async Task<int> ZieheZurueckAsync(
        TenantId firma, DateTimeOffset jetzt, CancellationToken cancellationToken = default)
    {
        var offene = await kontext.Stellen
            .AsTracking()
            .Where(kandidat => kandidat.TenantId == firma.Value && kandidat.Status != "closed")
            .ToListAsync(cancellationToken);

        foreach (var zeile in offene)
        {
            zeile.Status = "closed";
            zeile.UpdatedAt = jetzt.UtcDateTime;
        }

        return offene.Count;
    }

    private static Stelle ZumAggregat(StellenZeile zeile) => Stelle.Stelle_her(
        zeile.Id,
        new TenantId(zeile.TenantId),
        zeile.Title,
        zeile.Description,
        zeile.Location,
        Grad(zeile.RemoteMode),
        Anstellung(zeile.EmploymentType),
        Faehigkeitenliste.Stelle_her(JsonSerializer.Deserialize<List<string>>(zeile.Skills) ?? []),
        Stand(zeile.Status),
        new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero),
        zeile.PublishedAt is { } am ? new DateTimeOffset(am, TimeSpan.Zero) : null);

    private static StellenZeile ZurZeile(Stelle stelle)
    {
        var zeile = new StellenZeile
        {
            Id = stelle.Id,
            TenantId = stelle.Firma.Value,
            CreatedAt = stelle.AngelegtAm.UtcDateTime
        };

        Uebertrage(stelle, zeile);

        return zeile;
    }

    private static void Uebertrage(Stelle stelle, StellenZeile zeile)
    {
        zeile.Title = stelle.Titel;
        zeile.Description = stelle.Beschreibung;
        zeile.Location = stelle.Ort;
        zeile.RemoteMode = Stand(stelle.Remote);
        zeile.EmploymentType = Stand(stelle.Art);
        zeile.Skills = JsonSerializer.Serialize(stelle.Faehigkeiten.Werte);
        zeile.Status = Stand(stelle.Stand);
        zeile.UpdatedAt = stelle.GeaendertAm.UtcDateTime;
        zeile.PublishedAt = stelle.VeroeffentlichtAm?.UtcDateTime;
    }

    private static string Stand(Stellenstand stand) => stand switch
    {
        Stellenstand.Draft => "draft",
        Stellenstand.Published => "published",
        Stellenstand.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unbekannter Stand.")
    };

    private static Stellenstand Stand(string gespeichert) => gespeichert switch
    {
        "draft" => Stellenstand.Draft,
        "published" => Stellenstand.Published,
        "closed" => Stellenstand.Closed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(gespeichert), gespeichert, "Unbekannter Stand.")
    };

    private static string Stand(Remotegrad grad) => grad switch
    {
        Remotegrad.None => "none",
        Remotegrad.Hybrid => "hybrid",
        Remotegrad.Full => "full",
        _ => throw new ArgumentOutOfRangeException(nameof(grad), grad, "Unbekannter Grad.")
    };

    private static Remotegrad Grad(string gespeichert) => gespeichert switch
    {
        "none" => Remotegrad.None,
        "hybrid" => Remotegrad.Hybrid,
        "full" => Remotegrad.Full,
        _ => throw new ArgumentOutOfRangeException(
            nameof(gespeichert), gespeichert, "Unbekannter Grad.")
    };

    private static string Stand(Anstellungsart art) => art switch
    {
        Anstellungsart.FullTime => "full_time",
        Anstellungsart.PartTime => "part_time",
        Anstellungsart.Contract => "contract",
        Anstellungsart.Internship => "internship",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, "Unbekannte Art.")
    };

    private static Anstellungsart Anstellung(string gespeichert) => gespeichert switch
    {
        "full_time" => Anstellungsart.FullTime,
        "part_time" => Anstellungsart.PartTime,
        "contract" => Anstellungsart.Contract,
        "internship" => Anstellungsart.Internship,
        _ => throw new ArgumentOutOfRangeException(
            nameof(gespeichert), gespeichert, "Unbekannte Art.")
    };
}

/// <summary>Wo eine Seite weitergeht.</summary>
/// <remarks>
/// Zeitpunkt <em>und</em> Id: zwei Anzeigen in derselben Millisekunde bekämen
/// sonst je nach Laune der Datenbank eine andere Reihenfolge, und eine Seite
/// überspränge eine.
/// </remarks>
internal sealed record Zeiger(DateTime Am, Guid Id)
{
    public override string ToString() =>
        Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{Am:O}|{Id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Zeiger? Lies(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            var roh = text.Replace('-', '+').Replace('_', '/');
            roh = roh.PadRight(roh.Length + ((4 - (roh.Length % 4)) % 4), '=');

            var teile = System.Text.Encoding.UTF8
                .GetString(Convert.FromBase64String(roh))
                .Split('|');

            return teile.Length == 2
                   && DateTime.TryParse(
                       teile[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var am)
                   && Guid.TryParse(teile[1], out var id)
                ? new Zeiger(am, id)
                : null;
        }
        catch (FormatException)
        {
            // Ein unleserlicher Zeiger ist kein Fehler des Aufrufers, den man
            // ihm um die Ohren hauen müsste: er bekommt die erste Seite.
            return null;
        }
    }
}
