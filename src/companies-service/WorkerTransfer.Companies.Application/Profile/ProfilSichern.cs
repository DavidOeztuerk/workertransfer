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
    string Anzeigename,
    string UeberUns,
    string? Netzseite,
    IReadOnlyList<string>? Orte,
    IReadOnlyList<string>? Leistungen,
    string? Zeile1 = null,
    string? Postleitzahl = null,
    string? Ort = null,
    string? Land = null,
    string? Telefon = null) : IBefehl<EmployerProfile>;

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
                Domain.EmployerProfiles.Slug.Aus(request.Anzeigename), cancellationToken);

            profile = EmployerProfile.Lege_an(
                request.Firma, slug, request.Anzeigename, request.UeberUns,
                request.Netzseite, request.Orte, request.Leistungen, now,
                request.Zeile1, request.Postleitzahl, request.Ort, request.Land,
                request.Telefon);
        }
        else
        {
            vorhanden.Aendere(
                request.Anzeigename, request.UeberUns, request.Netzseite,
                request.Orte, request.Leistungen, now,
                request.Zeile1, request.Postleitzahl, request.Ort, request.Land,
                request.Telefon);

            profile = vorhanden;
        }

        await store.SaveAsync(profile, cancellationToken);

        return profile;
    }
}
