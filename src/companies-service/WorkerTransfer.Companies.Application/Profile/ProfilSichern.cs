using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Nachrichten;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Application.Profile;

/// <summary>Das eigene EmployerProfile schreiben.</summary>
/// <remarks>
/// <strong>Jedes Mitglied darf das</strong> — dieselbe Abwägung wie bei den
/// Jobs. Das Rollensystem kennt zwei Rollen, und <c>admin</c> heißt
/// „verwaltet die Mannschaft"; Inhalte sind die Arbeit der Mitglieder.
/// </remarks>
public sealed record SaveProfileCommand(
    TenantId Firma,
    string DisplayName,
    string About,
    string? Website,
    IReadOnlyList<string>? Locations,
    IReadOnlyList<string>? Benefits,
    string? Line1 = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? Phone = null) : IBefehl<EmployerProfile>;

/// <summary>Legt an oder ändert — und vergibt das Kürzel genau einmal.</summary>
public sealed class SaveProfileHandler(IProfileStore store, TimeProvider uhr)
    : IRequestHandler<SaveProfileCommand, EmployerProfile>
{
    /// <inheritdoc />
    public async Task<EmployerProfile> Handle(
        SaveProfileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = uhr.GetUtcNow();
        var vorhanden = await store.GetAsync(request.Firma, cancellationToken);
        EmployerProfile profile;

        if (vorhanden is null)
        {
            // Das Kürzel entsteht genau einmal, beim ersten Speichern. Danach
            // folgt es dem Anzeigenamen nicht mehr — sonst bräche jeder
            // geteilte Link auf die Karriere-Seite.
            var slug = await store.FreeSlugAsync(
                Domain.EmployerProfiles.Slug.Aus(request.DisplayName), cancellationToken);

            profile = EmployerProfile.Create(
                request.Firma, slug, request.DisplayName, request.About,
                request.Website, request.Locations, request.Benefits, now,
                request.Line1, request.PostalCode, request.City, request.Country,
                request.Phone);
        }
        else
        {
            vorhanden.Change(
                request.DisplayName, request.About, request.Website,
                request.Locations, request.Benefits, now,
                request.Line1, request.PostalCode, request.City, request.Country,
                request.Phone);

            profile = vorhanden;
        }

        await store.SaveAsync(profile, cancellationToken);

        return profile;
    }
}
