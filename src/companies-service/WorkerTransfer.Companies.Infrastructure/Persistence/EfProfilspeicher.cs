using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>company_profiles</c>.</summary>
public sealed class EfProfilspeicher(CompaniesDbContext kontext) : IProfilspeicher
{
    /// <inheritdoc />
    public async Task<Arbeitgeberprofil?> HoleAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Profile
            .FirstOrDefaultAsync(kandidat => kandidat.Id == firma.Value, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task<Arbeitgeberprofil?> HoleAsync(
        string kuerzel, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Profile
            .FirstOrDefaultAsync(kandidat => kandidat.Slug == kuerzel, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task<string> FreiesKuerzelAsync(
        string gewuenscht, CancellationToken cancellationToken = default)
    {
        var vergeben = await kontext.Profile
            .Where(kandidat => kandidat.Slug.StartsWith(gewuenscht))
            .Select(kandidat => kandidat.Slug)
            .ToListAsync(cancellationToken);

        var belegt = new HashSet<string>(vergeben, StringComparer.Ordinal);

        if (!belegt.Contains(gewuenscht))
        {
            return gewuenscht;
        }

        // Nicht die Mandanten-Kennung anhängen: die stünde dann in einer
        // Adresse, die weitergegeben wird.
        var zaehler = 2;

        while (belegt.Contains($"{gewuenscht}-{zaehler}"))
        {
            zaehler++;
        }

        return $"{gewuenscht}-{zaehler}";
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Arbeitgeberprofil profil, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profil);

        var vorhanden = await kontext.Profile
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == profil.Firma.Value, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Profile.Add(ZurZeile(profil));
            return;
        }

        Uebertrage(profil, vorhanden);
    }

    private static Arbeitgeberprofil ZumAggregat(ProfilZeile zeile) =>
        Arbeitgeberprofil.Stelle_her(
            new TenantId(zeile.Id),
            zeile.Slug,
            zeile.DisplayName,
            zeile.About,
            zeile.Website,
            Liste(zeile.Locations),
            Liste(zeile.Benefits),
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero),
            zeile.Line1,
            zeile.PostalCode,
            zeile.City,
            zeile.Country,
            zeile.Phone);

    private static ProfilZeile ZurZeile(Arbeitgeberprofil profil)
    {
        var zeile = new ProfilZeile
        {
            Id = profil.Firma.Value,
            // Das Kürzel steht nur hier und nicht in `Uebertrage`: es wird
            // einmal vergeben und danach nie wieder geschrieben.
            Slug = profil.Kuerzel,
            CreatedAt = profil.AngelegtAm.UtcDateTime
        };

        Uebertrage(profil, zeile);

        return zeile;
    }

    private static void Uebertrage(Arbeitgeberprofil profil, ProfilZeile zeile)
    {
        zeile.DisplayName = profil.Anzeigename;
        zeile.About = profil.UeberUns;
        zeile.Website = profil.Netzseite;
        zeile.Locations = JsonSerializer.Serialize(profil.Orte);
        zeile.Benefits = JsonSerializer.Serialize(profil.Leistungen);
        zeile.Line1 = profil.Zeile1;
        zeile.PostalCode = profil.Postleitzahl;
        zeile.City = profil.Ort;
        zeile.Country = profil.Land;
        zeile.Phone = profil.Telefon;
        zeile.UpdatedAt = profil.GeaendertAm.UtcDateTime;
    }

    private static IReadOnlyList<string> Liste(string jsonb) =>
        JsonSerializer.Deserialize<List<string>>(jsonb) ?? [];
}
