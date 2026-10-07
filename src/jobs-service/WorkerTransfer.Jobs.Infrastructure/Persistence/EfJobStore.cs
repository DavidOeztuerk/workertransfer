using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Reads and writes <c>jobs</c>.</summary>
public sealed class EfJobStore(JobsDbContext context) : IJobStore
{
    /// <inheritdoc />
    public async Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Jobs
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var existing = await context.Jobs
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == job.Id, cancellationToken);

        if (existing is null)
        {
            context.Jobs.Add(ToRow(job));
            return;
        }

        CopyInto(job, existing);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Job>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Jobs
            .Where(candidate => candidate.TenantId == company.Value)
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<JobPage> SearchAsync(
        int page,
        int pageLength,
        IReadOnlyList<string>? skills,
        string location,
        RemoteMode? remote,
        string searchTerm = "",
        Guid? company = null,
        string employment = "",
        SearchRadius? radius = null,
        CancellationToken cancellationToken = default)
    {
        // The state is filtered HERE and never by the caller: a `status=draft`
        // on the wire would be a way to read other companies' drafts.
        var query = context.Jobs.Where(candidate => candidate.Status == "published");

        if (!string.IsNullOrWhiteSpace(location))
        {
            query = query.Where(candidate => EF.Functions.ILike(candidate.Location, $"%{location}%"));
        }

        if (remote is { } mode)
        {
            var wanted = Word(mode);
            query = query.Where(candidate => candidate.RemoteMode == wanted);
        }

        // The company filter carries the careers page. Without it
        // `/careers/<slug>` showed the adverts of ALL companies under the name
        // of a single one — the interface always sent `company`, and this
        // service never read it.
        if (company is { } tenant)
        {
            query = query.Where(candidate => candidate.TenantId == tenant);
        }

        if (!string.IsNullOrWhiteSpace(employment))
        {
            query = query.Where(candidate => candidate.EmploymentType == employment);
        }

        // Free text over title AND description, regardless of upper and lower
        // case. Deliberately no full-text index: the set is small, and an index
        // would need a language decision nobody has taken — German and English
        // adverts stand side by side.
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var wanted = $"%{searchTerm.Trim()}%";
            query = query.Where(candidate =>
                EF.Functions.ILike(candidate.Title, wanted)
                || EF.Functions.ILike(candidate.Description, wanted));
        }

        // The order is publication time AND id. Two adverts in the same
        // millisecond would otherwise get a different order depending on the
        // database's mood, and an entry would stand on two pages or on none.
        var sorted = query
            .OrderByDescending(candidate => candidate.PublishedAt)
            .ThenByDescending(candidate => candidate.Id);

        // TWO FILTERS FORCE THE FULL PASS, and that is no oversight.
        //
        // Skills: filtered in memory, because the vocabulary stands in the code —
        // "Postgres" in the query has to hit "PostgreSQL" in the advert, and
        // this rule does not exist in SQL (ADR-0023).
        //
        // Distance: for the same reason. The advert stores a PLACE NAME, no
        // coordinates, and the table that makes a point of it stands in the code
        // (`PlaceLookup`). Two columns `latitude`/`longitude` would be faster and
        // WOULD HAVE A DEFECT that does not exist here: a place missing from the
        // table today and added tomorrow takes effect at once — with columns
        // every advert would need a back-fill, and whoever forgets it gets a
        // radius search that silently runs past old rows.
        //
        // With page numbers, filtering-on-one-page does not work any more anyway:
        // the total has to count the filtered set, otherwise "page 1 of 9"
        // stands over three hits. So filter first, then count, then cut.
        //
        // The price is a full query. At this size that is cheap. Should it
        // become expensive, the canonical skill and the point would belong in
        // columns of their own — with a back-fill, and not the rule rebuilt in
        // SQL.
        if (skills is { Count: > 0 } || radius is not null)
        {
            var all = await sorted.ToListAsync(cancellationToken);
            IEnumerable<Job> hits = all.Select(ToAggregate);

            if (skills is { Count: > 0 })
            {
                var wanted = SkillList.From(skills).Values;

                hits = hits.Where(job => wanted.All(single =>
                    job.Skills.Values.Contains(single, StringComparer.OrdinalIgnoreCase)));
            }

            var unlocated = 0;

            if (radius is { } circle)
            {
                var centre = new PlacePoint(circle.Latitude, circle.Longitude);
                var inside = new List<Job>();

                foreach (var job in hits)
                {
                    // Fully remote is ALWAYS included. From where such a job can
                    // be reached is no question of distance — and hiding it
                    // because of a location filter would hit exactly the adverts
                    // that are the most interesting for somebody outside the
                    // conurbations.
                    if (job.Remote is RemoteMode.Full)
                    {
                        inside.Add(job);
                        continue;
                    }

                    // The postal code FIRST — it is unique, the place name need
                    // not be.
                    var point = PlaceLookup.Find(job.PostalCode, job.Location);

                    // An unknown place does NOT mean "far away" but "we know
                    // nothing about it". The difference is counted and passed
                    // out instead of being made to disappear.
                    if (point is not { } target)
                    {
                        unlocated++;
                        continue;
                    }

                    if (PlaceLookup.DistanceKm(centre, target) <= circle.RadiusKm)
                    {
                        inside.Add(job);
                    }
                }

                hits = inside;
            }

            var list = hits as List<Job> ?? [.. hits];

            return new JobPage(
                [.. list.Skip((page - 1) * pageLength).Take(pageLength)],
                list.Count,
                unlocated);
        }

        // Without these two, the database counts and cuts.
        var total = await sorted.CountAsync(cancellationToken);

        var rows = await sorted
            .Skip((page - 1) * pageLength)
            .Take(pageLength)
            .ToListAsync(cancellationToken);

        return new JobPage([.. rows.Select(ToAggregate)], total);
    }

    /// <inheritdoc />
    public async Task<int> WithdrawAsync(
        TenantId company, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var open = await context.Jobs
            .AsTracking()
            .Where(candidate => candidate.TenantId == company.Value && candidate.Status != "closed")
            .ToListAsync(cancellationToken);

        foreach (var row in open)
        {
            row.Status = "closed";
            row.UpdatedAt = now.UtcDateTime;
        }

        return open.Count;
    }

    private static Job ToAggregate(JobRow row) => Job.Restore(
        row.Id,
        new TenantId(row.TenantId),
        row.Title,
        row.Description,
        row.Location,
        row.PostalCode,
        ToRemoteMode(row.RemoteMode),
        ToEmployment(row.EmploymentType),
        SkillList.Restore(JsonSerializer.Deserialize<List<string>>(row.Skills) ?? []),
        ToState(row.Status),
        new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
        row.PublishedAt is { } at ? new DateTimeOffset(at, TimeSpan.Zero) : null,
        row.SalaryMin is { } low && row.SalaryMax is { } high
            ? SalaryRange.Restore(low, high, row.SalaryPeriod switch
            {
                "month" => SalaryPeriod.Month,
                "hour" => SalaryPeriod.Hour,
                _ => SalaryPeriod.Year
            })
            : null);

    private static JobRow ToRow(Job job)
    {
        var row = new JobRow
        {
            Id = job.Id,
            TenantId = job.Company.Value,
            CreatedAt = job.CreatedAt.UtcDateTime
        };

        CopyInto(job, row);

        return row;
    }

    private static void CopyInto(Job job, JobRow row)
    {
        row.Title = job.Title;
        row.Description = job.Description;
        row.Location = job.Location;
        row.PostalCode = job.PostalCode;
        row.RemoteMode = Word(job.Remote);
        row.EmploymentType = Word(job.Employment);
        row.Skills = JsonSerializer.Serialize(job.Skills.Values);
        row.Status = Word(job.State);
        row.UpdatedAt = job.UpdatedAt.UtcDateTime;
        row.PublishedAt = job.PublishedAt?.UtcDateTime;
        row.SalaryMin = job.Salary?.Minimum;
        row.SalaryMax = job.Salary?.Maximum;
        row.SalaryPeriod = job.Salary?.Period.ToString().ToLowerInvariant();
    }

    // The stored words below are the column values of every existing row and
    // the same words the API sends. They are written out on purpose instead of
    // derived from the member names, so a rename of a member cannot move them.

    private static string Word(JobState state) => state switch
    {
        JobState.Draft => "draft",
        JobState.Published => "published",
        JobState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown state.")
    };

    private static JobState ToState(string stored) => stored switch
    {
        "draft" => JobState.Draft,
        "published" => JobState.Published,
        "closed" => JobState.Closed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stored), stored, "Unknown state.")
    };

    private static string Word(RemoteMode mode) => mode switch
    {
        RemoteMode.None => "none",
        RemoteMode.Hybrid => "hybrid",
        RemoteMode.Full => "full",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown remote mode.")
    };

    private static RemoteMode ToRemoteMode(string stored) => stored switch
    {
        "none" => RemoteMode.None,
        "hybrid" => RemoteMode.Hybrid,
        "full" => RemoteMode.Full,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stored), stored, "Unknown remote mode.")
    };

    private static string Word(EmploymentKind kind) => kind switch
    {
        EmploymentKind.FullTime => "full_time",
        EmploymentKind.PartTime => "part_time",
        EmploymentKind.Contract => "contract",
        EmploymentKind.Internship => "internship",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown employment kind.")
    };

    private static EmploymentKind ToEmployment(string stored) => stored switch
    {
        "full_time" => EmploymentKind.FullTime,
        "part_time" => EmploymentKind.PartTime,
        "contract" => EmploymentKind.Contract,
        "internship" => EmploymentKind.Internship,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stored), stored, "Unknown employment kind.")
    };
}

/// <summary>Where a page continues.</summary>
/// <remarks>
/// Time <em>and</em> id: two adverts in the same millisecond would otherwise get
/// a different order depending on the database's mood, and a page would skip
/// one.
/// </remarks>
internal sealed record Cursor(DateTime At, Guid Id)
{
    public override string ToString() =>
        Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{At:O}|{Id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Cursor? Read(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            var raw = text.Replace('-', '+').Replace('_', '/');
            raw = raw.PadRight(raw.Length + ((4 - (raw.Length % 4)) % 4), '=');

            var parts = System.Text.Encoding.UTF8
                .GetString(Convert.FromBase64String(raw))
                .Split('|');

            return parts.Length == 2
                   && DateTime.TryParse(
                       parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                   && Guid.TryParse(parts[1], out var id)
                ? new Cursor(at, id)
                : null;
        }
        catch (FormatException)
        {
            // An unreadable cursor is no error of the caller that would have to
            // be thrown back at them: they get the first page.
            return null;
        }
    }
}
