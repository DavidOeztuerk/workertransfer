using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Nachrichten;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

namespace WorkerTransfer.Companies.Application.Profile;

/// <summary>Das eigene Arbeitgeberprofil schreiben.</summary>
/// <remarks>
/// <strong>Jedes Mitglied darf das</strong> — dieselbe Abwägung wie bei den
/// Stellen. Das Rollensystem kennt zwei Rollen, und <c>admin</c> heißt
/// „verwaltet die Mannschaft"; Inhalte sind die Arbeit der Mitglieder.
/// </remarks>
public sealed record ProfilSichernBefehl(
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
    string? Telefon = null) : IBefehl<Arbeitgeberprofil>;

/// <summary>Legt an oder ändert — und vergibt das Kürzel genau einmal.</summary>
public sealed class ProfilSichernHandler(IProfilspeicher speicher, TimeProvider uhr)
    : IRequestHandler<ProfilSichernBefehl, Arbeitgeberprofil>
{
    /// <inheritdoc />
    public async Task<Arbeitgeberprofil> Handle(
        ProfilSichernBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();
        var vorhanden = await speicher.HoleAsync(request.Firma, cancellationToken);
        Arbeitgeberprofil profil;

        if (vorhanden is null)
        {
            // Das Kürzel entsteht genau einmal, beim ersten Speichern. Danach
            // folgt es dem Anzeigenamen nicht mehr — sonst bräche jeder
            // geteilte Link auf die Karriere-Seite.
            var kuerzel = await speicher.FreiesKuerzelAsync(
                Domain.Arbeitgeberprofile.Kuerzel.Aus(request.Anzeigename), cancellationToken);

            profil = Arbeitgeberprofil.Lege_an(
                request.Firma, kuerzel, request.Anzeigename, request.UeberUns,
                request.Netzseite, request.Orte, request.Leistungen, jetzt,
                request.Zeile1, request.Postleitzahl, request.Ort, request.Land,
                request.Telefon);
        }
        else
        {
            vorhanden.Aendere(
                request.Anzeigename, request.UeberUns, request.Netzseite,
                request.Orte, request.Leistungen, jetzt,
                request.Zeile1, request.Postleitzahl, request.Ort, request.Land,
                request.Telefon);

            profil = vorhanden;
        }

        await speicher.SichereAsync(profil, cancellationToken);

        return profil;
    }
}
