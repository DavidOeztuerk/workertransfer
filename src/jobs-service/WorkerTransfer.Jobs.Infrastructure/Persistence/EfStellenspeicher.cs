using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>jobs</c>.</summary>
public sealed class EfJobStore(JobsDbContext context) : IJobStore
{
    /// <inheritdoc />
    public async Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Jobs
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var vorhanden = await context.Jobs
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == job.Id, cancellationToken);

        if (vorhanden is null)
        {
            context.Jobs.Add(ZurZeile(job));
            return;
        }

        Uebertrage(job, vorhanden);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Job>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Jobs
            .Where(kandidat => kandidat.TenantId == company.Value)
            .OrderByDescending(kandidat => kandidat.UpdatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<JobPage> SearchAsync(
        int page,
        int count,
        IReadOnlyList<string>? skills,
        string ort,
        Remotegrad? remote,
        string suchbegriff = "",
        Guid? company = null,
        string beschaeftigung = "",
        Umkreis? umkreis = null,
        CancellationToken cancellationToken = default)
    {
        // Der State wird HIER gefiltert und nie vom Aufrufer: ein
        // `status=draft` auf der Leitung wäre ein Weg, fremde Entwürfe zu
        // lesen.
        var abfrage = context.Jobs.Where(kandidat => kandidat.Status == "published");

        if (!string.IsNullOrWhiteSpace(ort))
        {
            abfrage = abfrage.Where(kandidat => EF.Functions.ILike(kandidat.Location, $"%{ort}%"));
        }

        if (remote is { } grad)
        {
            var gesucht = State(grad);
            abfrage = abfrage.Where(kandidat => kandidat.RemoteMode == gesucht);
        }

        // Der Firmenfilter traegt die Karriereseite. Ohne ihn zeigte
        // `/careers/<kuerzel>` die Anzeigen ALLER Unternehmen unter dem Namen
        // eines einzigen — die Oberflaeche schickte `company` seit jeher, und
        // dieser Dienst hat ihn nie gelesen.
        if (company is { } tenant)
        {
            abfrage = abfrage.Where(kandidat => kandidat.TenantId == tenant);
        }

        if (!string.IsNullOrWhiteSpace(beschaeftigung))
        {
            abfrage = abfrage.Where(kandidat => kandidat.EmploymentType == beschaeftigung);
        }

        // Freitext ueber Titel UND Beschreibung, ohne Ruecksicht auf Gross- und
        // Kleinschreibung. Bewusst kein Volltextindex: die Menge ist klein, und
        // ein Index brauchte eine Sprachentscheidung, die niemand getroffen hat
        // — deutsche und englische Anzeigen stehen nebeneinander.
        if (!string.IsNullOrWhiteSpace(suchbegriff))
        {
            var gesucht = $"%{suchbegriff.Trim()}%";
            abfrage = abfrage.Where(kandidat =>
                EF.Functions.ILike(kandidat.Title, gesucht)
                || EF.Functions.ILike(kandidat.Description, gesucht));
        }

        // Die Reihenfolge ist Veröffentlichungszeitpunkt UND Id. Zwei Anzeigen in
        // derselben Millisekunde bekämen sonst je nach Laune der Datenbank eine
        // andere Reihenfolge, und ein Eintrag stünde auf zwei Seiten oder auf
        // keiner.
        var sortiert = abfrage
            .OrderByDescending(kandidat => kandidat.PublishedAt)
            .ThenByDescending(kandidat => kandidat.Id);

        // ZWEI FILTER ZWINGEN ZUM VOLLEN DURCHGANG, und das ist kein Versehen.
        //
        // Fähigkeiten: gefiltert wird im Speicher, weil der Vocabulary im Code
        // steht — „Postgres" in der Anfrage muss „PostgreSQL" in der Anzeige
        // treffen, und diese Regel gibt es in SQL nicht (ADR-0023).
        //
        // Entfernung: aus demselben Grund. Die Anzeige speichert einen
        // ORTSNAMEN, keine Koordinaten, und die Tabelle, die daraus einen Punkt
        // macht, steht im Code (`PlaceLookup`). Zwei Spalten `latitude`/`longitude`
        // wären schneller und HÄTTEN EINEN FEHLER, den es hier nicht gibt: ein
        // Ort, der der Tabelle heute fehlt und morgen ergänzt wird, wirkt sofort
        // — mit Spalten bräuchte jede Anzeige eine Nachwanderung, und wer sie
        // vergisst, bekommt eine Umkreissuche, die still an alten Zeilen
        // vorbeiläuft.
        //
        // Mit Seitennummern geht Filtern-auf-einer-Seite ohnehin nicht mehr: die
        // Gesamtzahl muss die gefilterte Menge zählen, sonst steht „Seite 1 von
        // 9" über drei Treffern. Also erst filtern, dann zählen, dann schneiden.
        //
        // Der Preis ist eine volle Abfrage. Bei dieser Menge ist das billig.
        // Würde es teuer, gehörten die kanonische Fähigkeit und der Punkt in
        // eigene Spalten — samt Nachwanderung, und nicht die Regel in SQL
        // nachgebaut.
        if (skills is { Count: > 0 } || umkreis is not null)
        {
            var alle = await sortiert.ToListAsync(cancellationToken);
            IEnumerable<Job> passend = alle.Select(ZumAggregat);

            if (skills is { Count: > 0 })
            {
                var gesucht = SkillList.Aus(skills).Werte;

                passend = passend.Where(job => gesucht.All(einzeln =>
                    job.Faehigkeiten.Werte.Contains(einzeln, StringComparer.OrdinalIgnoreCase)));
            }

            var ohneOrt = 0;

            if (umkreis is { } kreis)
            {
                var mitte = new PlacePoint(kreis.Breite, kreis.Laenge);
                var drin = new List<Job>();

                foreach (var job in passend)
                {
                    // Voll remote ist IMMER dabei. Von wo aus so eine Job
                    // erreichbar ist, ist keine Frage der Entfernung — und sie
                    // wegen eines Ortsfilters zu verstecken träfe genau die
                    // Anzeigen, die für jemanden ausserhalb der Ballungsräume
                    // die interessantesten sind.
                    if (job.Remote is Remotegrad.Full)
                    {
                        drin.Add(job);
                        continue;
                    }

                    // Die Postleitzahl ZUERST — sie ist eindeutig, der
                    // Ortsname muss es nicht sein.
                    var punkt = PlaceLookup.Find(job.Postleitzahl, job.Ort);

                    // Unbekannter Ort heisst NICHT „weit weg", sondern „darüber
                    // wissen wir nichts". Der Unterschied wird gezählt und
                    // hinausgereicht, statt ihn verschwinden zu lassen.
                    if (punkt is not { } target)
                    {
                        ohneOrt++;
                        continue;
                    }

                    if (PlaceLookup.DistanceKm(mitte, target) <= kreis.RadiusKm)
                    {
                        drin.Add(job);
                    }
                }

                passend = drin;
            }

            var list = passend as List<Job> ?? [.. passend];

            return new JobPage(
                [.. list.Skip((page - 1) * count).Take(count)],
                list.Count,
                ohneOrt);
        }

        // Ohne diese beiden zählt und schneidet die Datenbank.
        var gesamt = await sortiert.CountAsync(cancellationToken);

        var rows = await sortiert
            .Skip((page - 1) * count)
            .Take(count)
            .ToListAsync(cancellationToken);

        return new JobPage([.. rows.Select(ZumAggregat)], gesamt);
    }

    /// <inheritdoc />
    public async Task<int> WithdrawAsync(
        TenantId company, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var offene = await context.Jobs
            .AsTracking()
            .Where(kandidat => kandidat.TenantId == company.Value && kandidat.Status != "closed")
            .ToListAsync(cancellationToken);

        foreach (var row in offene)
        {
            row.Status = "closed";
            row.UpdatedAt = now.UtcDateTime;
        }

        return offene.Count;
    }

    private static Job ZumAggregat(JobRow row) => Job.Stelle_her(
        row.Id,
        new TenantId(row.TenantId),
        row.Title,
        row.Description,
        row.Location,
        row.PostalCode,
        Grad(row.RemoteMode),
        Anstellung(row.EmploymentType),
        SkillList.Stelle_her(JsonSerializer.Deserialize<List<string>>(row.Skills) ?? []),
        State(row.Status),
        new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
        row.PublishedAt is { } am ? new DateTimeOffset(am, TimeSpan.Zero) : null);

    private static JobRow ZurZeile(Job job)
    {
        var row = new JobRow
        {
            Id = job.Id,
            TenantId = job.Firma.Value,
            CreatedAt = job.AngelegtAm.UtcDateTime
        };

        Uebertrage(job, row);

        return row;
    }

    private static void Uebertrage(Job job, JobRow row)
    {
        row.Title = job.Titel;
        row.Description = job.Beschreibung;
        row.Location = job.Ort;
        row.PostalCode = job.Postleitzahl;
        row.RemoteMode = State(job.Remote);
        row.EmploymentType = State(job.Art);
        row.Skills = JsonSerializer.Serialize(job.Faehigkeiten.Werte);
        row.Status = State(job.State);
        row.UpdatedAt = job.GeaendertAm.UtcDateTime;
        row.PublishedAt = job.VeroeffentlichtAm?.UtcDateTime;
    }

    private static string State(JobState state) => state switch
    {
        JobState.Draft => "draft",
        JobState.Published => "published",
        JobState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unbekannter Stand.")
    };

    private static JobState State(string gespeichert) => gespeichert switch
    {
        "draft" => JobState.Draft,
        "published" => JobState.Published,
        "closed" => JobState.Closed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(gespeichert), gespeichert, "Unbekannter Stand.")
    };

    private static string State(Remotegrad grad) => grad switch
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

    private static string State(EmploymentKind kind) => kind switch
    {
        EmploymentKind.FullTime => "full_time",
        EmploymentKind.PartTime => "part_time",
        EmploymentKind.Contract => "contract",
        EmploymentKind.Internship => "internship",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unbekannte Art.")
    };

    private static EmploymentKind Anstellung(string gespeichert) => gespeichert switch
    {
        "full_time" => EmploymentKind.FullTime,
        "part_time" => EmploymentKind.PartTime,
        "contract" => EmploymentKind.Contract,
        "internship" => EmploymentKind.Internship,
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
internal sealed record Cursor(DateTime Am, Guid Id)
{
    public override string ToString() =>
        Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{Am:O}|{Id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Cursor? Lies(string? text)
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
                ? new Cursor(am, id)
                : null;
        }
        catch (FormatException)
        {
            // Ein unleserlicher Cursor ist kein Fehler des Aufrufers, den man
            // ihm um die Ohren hauen müsste: er bekommt die erste Seite.
            return null;
        }
    }
}
