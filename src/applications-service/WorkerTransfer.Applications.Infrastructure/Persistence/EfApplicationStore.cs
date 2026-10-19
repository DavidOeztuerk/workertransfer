using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Reads and writes <c>applications</c>.</summary>
/// <remarks>
/// Rows read come back as a <em>new</em> aggregate, not as a tracked row. A
/// change therefore reaches the database only via <see cref="SaveAsync"/> —
/// whoever forgets it loses the write, and that is to show in the test just as
/// it would in operation.
/// </remarks>
public sealed class EfApplicationStore(ApplicationsDbContext context) : IApplicationStore
{
    /// <inheritdoc />
    public async Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Applications
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<JobApplication?> GetAsync(
        Guid job, SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Applications.FirstOrDefaultAsync(
            candidate => candidate.JobId == job && candidate.SubjectId == subject.Value,
            cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        JobApplication application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);

        var existing = await context.Applications
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == application.Id, cancellationToken);

        if (existing is null)
        {
            context.Applications.Add(ToRow(application));
            return;
        }

        CopyInto(application, existing);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobApplication>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Applications
            .Where(candidate => candidate.SubjectId == subject.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobApplication>> ForJobAsync(
        Guid job, TenantId company, CancellationToken cancellationToken = default)
    {
        // The company stands in the query, not in a filter afterwards: a foreign
        // job delivers an empty list instead of giving away that it exists.
        var rows = await context.Applications
            .Where(candidate => candidate.JobId == job && candidate.TenantId == company.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ApplicationState, int>> CountAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var counted = await context.Applications
            .Where(candidate => candidate.TenantId == company.Value)
            .GroupBy(candidate => candidate.Status)
            .Select(group => new { State = group.Key, Size = group.Count() })
            .ToListAsync(cancellationToken);

        var numbers = new Dictionary<ApplicationState, int>();

        foreach (var entry in counted)
        {
            // A word that is no state is skipped rather than thrown. These
            // numbers are a convenience; no list the company sees one by one
            // anyway is to fail on them.
            if (ApplicationStates.Parse(entry.State) is { } state)
            {
                numbers[state] = entry.Size;
            }
        }

        return numbers;
    }

    private static JobApplication ToAggregate(ApplicationRow row) =>
        JobApplication.Restore(
            row.Id,
            row.JobId,
            new TenantId(row.TenantId),
            new SubjectId(row.SubjectId),
            row.Message,
            new Enclosures(
                row.SharesResume,
                row.SharesPortfolio,
                JsonSerializer.Deserialize<List<Guid>>(row.Documents) ?? []),
            ApplicationStates.Parse(row.Status)
            ?? throw new InvalidOperationException(
                $"Unknown application state in the database: {row.Status}"),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.AnsweredAt is { } answered
                ? new DateTimeOffset(answered, TimeSpan.Zero)
                : null,
            ReadContact(row.ApplicantContact));

    private static ApplicationRow ToRow(JobApplication application)
    {
        var row = new ApplicationRow
        {
            Id = application.Id,
            JobId = application.Job,
            TenantId = application.Company.Value,
            SubjectId = application.Subject.Value,
            CreatedAt = application.CreatedAt.UtcDateTime
        };

        CopyInto(application, row);

        return row;
    }

    private static void CopyInto(JobApplication application, ApplicationRow row)
    {
        // Job, company, person and the moment of sending do not stand here: they
        // never change, and an assignment that could would be the way to
        // attribute an application to someone else afterwards.
        row.Message = application.Message;
        row.SharesResume = application.Enclosures.CurriculumVitae;
        row.SharesPortfolio = application.Enclosures.Portfolio;
        row.Documents = JsonSerializer.Serialize(application.Enclosures.Documents);
        row.Status = ApplicationStates.Word(application.State);
        row.UpdatedAt = application.UpdatedAt.UtcDateTime;
        row.AnsweredAt = application.AnsweredAt?.UtcDateTime;
        row.ApplicantContact = JsonSerializer.Serialize(new StoredContact(application.Contact));
    }

    private static ApplicantContact ReadContact(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "{}")
        {
            return ApplicantContact.Empty;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredContact>(raw);
            return stored is null
                ? ApplicantContact.Empty
                : new ApplicantContact(
                    stored.FullName ?? string.Empty,
                    stored.Line1 ?? string.Empty,
                    stored.Line2 ?? string.Empty,
                    stored.PostalCode ?? string.Empty,
                    stored.City ?? string.Empty,
                    string.IsNullOrEmpty(stored.Country) ? "DE" : stored.Country,
                    stored.Phone ?? string.Empty,
                    stored.Email ?? string.Empty);
        }
        catch (JsonException)
        {
            return ApplicantContact.Empty;
        }
    }

    /// <summary>The letterhead as the jsonb column holds it.</summary>
    /// <remarks>
    /// The <c>JsonPropertyName</c>s ARE the stored keys, the same as on the wire
    /// (<c>ApplicantContactV1</c>): renaming a property here moves nothing in the
    /// column.
    /// </remarks>
    private sealed record StoredContact(
        [property: JsonPropertyName("full_name")] string? FullName = null,
        [property: JsonPropertyName("line1")] string? Line1 = null,
        [property: JsonPropertyName("line2")] string? Line2 = null,
        [property: JsonPropertyName("postal_code")] string? PostalCode = null,
        [property: JsonPropertyName("city")] string? City = null,
        [property: JsonPropertyName("country")] string? Country = null,
        [property: JsonPropertyName("phone")] string? Phone = null,
        [property: JsonPropertyName("email")] string? Email = null)
    {
        public StoredContact() : this(null, null, null, null, null, null, null, null)
        {
        }

        public StoredContact(ApplicantContact contact)
            : this(
                contact.FullName,
                contact.Line1,
                contact.Line2,
                contact.PostalCode,
                contact.City,
                contact.Country,
                contact.Phone,
                contact.Email)
        {
        }
    }
}
