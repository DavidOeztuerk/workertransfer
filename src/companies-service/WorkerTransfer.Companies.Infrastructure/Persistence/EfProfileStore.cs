using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Reads and writes <c>company_profiles</c>.</summary>
public sealed class EfProfileStore(CompaniesDbContext context) : IProfileStore
{
    /// <inheritdoc />
    public async Task<EmployerProfile?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.Profile
            .FirstOrDefaultAsync(candidate => candidate.Id == company.Value, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<EmployerProfile?> GetAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var row = await context.Profile
            .FirstOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<string> FreeSlugAsync(
        string wanted, CancellationToken cancellationToken = default)
    {
        var assigned = await context.Profile
            .Where(candidate => candidate.Slug.StartsWith(wanted))
            .Select(candidate => candidate.Slug)
            .ToListAsync(cancellationToken);

        var taken = new HashSet<string>(assigned, StringComparer.Ordinal);

        if (!taken.Contains(wanted))
        {
            return wanted;
        }

        // Do not append the tenant id: it would then sit in an address that
        // people pass around.
        var counter = 2;

        while (taken.Contains($"{wanted}-{counter}"))
        {
            counter++;
        }

        return $"{wanted}-{counter}";
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        EmployerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var existing = await context.Profile
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == profile.Company.Value, cancellationToken);

        if (existing is null)
        {
            context.Profile.Add(ToRow(profile));
            return;
        }

        Transfer(profile, existing);
    }

    private static EmployerProfile ToAggregate(ProfileRow row) =>
        EmployerProfile.Restore(
            new TenantId(row.Id),
            row.Slug,
            row.DisplayName,
            row.About,
            row.Website,
            List(row.Locations),
            List(row.Benefits),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.Line1,
            row.PostalCode,
            row.City,
            row.Country,
            row.Phone);

    private static ProfileRow ToRow(EmployerProfile profile)
    {
        var row = new ProfileRow
        {
            Id = profile.Company.Value,
            // Das Kürzel steht nur hier und nicht in `Transfer`: es wird
            // einmal assigned und danach nie wieder geschrieben.
            Slug = profile.Slug,
            CreatedAt = profile.CreatedAt.UtcDateTime
        };

        Transfer(profile, row);

        return row;
    }

    private static void Transfer(EmployerProfile profile, ProfileRow row)
    {
        row.DisplayName = profile.DisplayName;
        row.About = profile.About;
        row.Website = profile.Website;
        row.Locations = JsonSerializer.Serialize(profile.Locations);
        row.Benefits = JsonSerializer.Serialize(profile.Benefits);
        row.Line1 = profile.Line1;
        row.PostalCode = profile.PostalCode;
        row.City = profile.City;
        row.Country = profile.Country;
        row.Phone = profile.Phone;
        row.UpdatedAt = profile.ChangedAt.UtcDateTime;
    }

    private static IReadOnlyList<string> List(string jsonb) =>
        JsonSerializer.Deserialize<List<string>>(jsonb) ?? [];
}
