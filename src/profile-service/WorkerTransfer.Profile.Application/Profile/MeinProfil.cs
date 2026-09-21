using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Das eigene PersonProfile — ohne Ledger-Abfrage.</summary>
/// <param name="Wer">Die Person aus dem Token.</param>
/// <remarks>
/// Die eigene Einwilligung zu prüfen, um sich selbst zu sehen, wäre nicht nur
/// ein überflüssiger Umlauf, sondern falsch: wer nichts freigegeben hat, könnte
/// sein PersonProfile sonst nicht mehr bearbeiten (ADR-0020 §5).
/// </remarks>
public sealed record MyProfileQuery(SubjectId Wer) : IAbfrage<PersonProfile?>;

/// <summary>Liest das eigene PersonProfile, oder gibt <c>null</c>.</summary>
/// <remarks>
/// <c>null</c> ist hier kein Fehler: „noch keins angelegt“ ist ein Zustand, den
/// die Oberfläche als leeres Formular zeigt.
/// </remarks>
public sealed class MyProfileHandler(IProfileStore store)
    : IRequestHandler<MyProfileQuery, PersonProfile?>
{
    /// <inheritdoc />
    public Task<PersonProfile?> Handle(MyProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Wer, cancellationToken);
    }
}
