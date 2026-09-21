using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>company_profiles</c>.</summary>
public sealed class EfProfileStore(CompaniesDbContext context) : IProfileStore
{
    /// <inheritdoc />
    public async Task<EmployerProfile?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.Profile
            .FirstOrDefaultAsync(kandidat => kandidat.Id == company.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<EmployerProfile?> GetAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var row = await context.Profile
            .FirstOrDefaultAsync(kandidat => kandidat.Slug == slug, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<string> FreeSlugAsync(
        string wanted, CancellationToken cancellationToken = default)
    {
        var vergeben = await context.Profile
            .Where(kandidat => kandidat.Slug.StartsWith(wanted))
            .Select(kandidat => kandidat.Slug)
            .ToListAsync(cancellationToken);

        var belegt = new HashSet<string>(vergeben, StringComparer.Ordinal);

        if (!belegt.Contains(wanted))
        {
            return wanted;
        }

        // Nicht die Mandanten-Kennung anhängen: die stünde dann in einer
        // Adresse, die weitergegeben wird.
        var zaehler = 2;

        while (belegt.Contains($"{wanted}-{zaehler}"))
        {
            zaehler++;
        }

        return $"{wanted}-{zaehler}";
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        EmployerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var vorhanden = await context.Profile
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == profile.Firma.Value, cancellationToken);

        if (vorhanden is null)
        {
            context.Profile.Add(ZurZeile(profile));
            return;
        }

        Uebertrage(profile, vorhanden);
    }

    private static EmployerProfile ZumAggregat(ProfileRow row) =>
        EmployerProfile.Restore(
            new TenantId(row.Id),
            row.Slug,
            row.DisplayName,
            row.About,
            row.Website,
            Liste(row.Locations),
            Liste(row.Benefits),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.Line1,
            row.PostalCode,
            row.City,
            row.Country,
            row.Phone);

    private static ProfileRow ZurZeile(EmployerProfile profile)
    {
        var row = new ProfileRow
        {
            Id = profile.Firma.Value,
            // Das Kürzel steht nur hier und nicht in `Uebertrage`: es wird
            // einmal vergeben und danach nie wieder geschrieben.
            Slug = profile.Slug,
            CreatedAt = profile.CreatedAt.UtcDateTime
        };

        Uebertrage(profile, row);

        return row;
    }

    private static void Uebertrage(EmployerProfile profile, ProfileRow row)
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

    private static IReadOnlyList<string> Liste(string jsonb) =>
        JsonSerializer.Deserialize<List<string>>(jsonb) ?? [];
}
