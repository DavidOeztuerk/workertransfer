using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Nachrichten;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

namespace WorkerTransfer.Companies.Application.Profile;

/// <summary>Das eigene Profil.</summary>
/// <remarks>
/// Ohne Ergebnistyp: „noch keins angelegt" ist ein Zustand, kein Fehler, und
/// die Oberfläche zeigt darauf ein leeres Formular.
/// </remarks>
public sealed record MeinProfilAbfrage(TenantId Firma) : IAbfrage<Arbeitgeberprofil?>;

/// <summary>Das öffentliche Profil eines Unternehmens.</summary>
/// <remarks>
/// Anders als beim eigenen ist „noch keins" hier kein Formular, sondern eine
/// Stelle, die anonym bleibt — der Endpunkt antwortet 404. Ein Profil zu
/// erzwingen, bevor jemand ausschreiben darf, wäre eine Kopplung zwischen zwei
/// Diensten für eine Regel, die niemand verlangt hat.
/// </remarks>
public sealed record OeffentlichesProfilAbfrage(TenantId Firma) : IAbfrage<Arbeitgeberprofil?>;

/// <summary>Die Karriere-Seite hinter einem Kürzel.</summary>
public sealed record ProfilNachKuerzelAbfrage(string Kuerzel) : IAbfrage<Arbeitgeberprofil?>;

/// <summary>Beantwortet die drei Lesefragen.</summary>
/// <remarks>
/// Kein Consent-Aufruf, und das ist kein Versehen: ein Arbeitgeberprofil ist
/// eine Aussage des Unternehmens über sich selbst, und es gibt niemanden, der
/// einwilligen könnte. Es öffentlich zu machen ist sein Zweck; es hinter eine
/// Anmeldung zu legen wäre das Gegenteil — genau wie bei den Stellen.
/// </remarks>
public sealed class Profilabfragen(IProfilspeicher speicher) :
    IRequestHandler<MeinProfilAbfrage, Arbeitgeberprofil?>,
    IRequestHandler<OeffentlichesProfilAbfrage, Arbeitgeberprofil?>,
    IRequestHandler<ProfilNachKuerzelAbfrage, Arbeitgeberprofil?>
{
    /// <inheritdoc />
    public Task<Arbeitgeberprofil?> Handle(
        MeinProfilAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Firma, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Arbeitgeberprofil?> Handle(
        OeffentlichesProfilAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Firma, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Arbeitgeberprofil?> Handle(
        ProfilNachKuerzelAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Kuerzel, cancellationToken);
    }
}
