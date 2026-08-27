using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Das eigene Profil — ohne Ledger-Abfrage.</summary>
/// <param name="Wer">Die Person aus dem Token.</param>
/// <remarks>
/// Die eigene Einwilligung zu prüfen, um sich selbst zu sehen, wäre nicht nur
/// ein überflüssiger Umlauf, sondern falsch: wer nichts freigegeben hat, könnte
/// sein Profil sonst nicht mehr bearbeiten (ADR-0020 §5).
/// </remarks>
public sealed record MeinProfilAbfrage(SubjectId Wer) : IAbfrage<Profil?>;

/// <summary>Liest das eigene Profil, oder gibt <c>null</c>.</summary>
/// <remarks>
/// <c>null</c> ist hier kein Fehler: „noch keins angelegt“ ist ein Zustand, den
/// die Oberfläche als leeres Formular zeigt.
/// </remarks>
public sealed class MeinProfilHandler(IProfilspeicher speicher)
    : IRequestHandler<MeinProfilAbfrage, Profil?>
{
    /// <inheritdoc />
    public Task<Profil?> Handle(MeinProfilAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Wer, cancellationToken);
    }
}
