using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Das Profil eines anderen Menschen.</summary>
/// <param name="Wer">Wessen Profil gesucht wird.</param>
/// <param name="Firma">Das Unternehmen des Aufrufers — aus dem Token, nie aus der Anfrage.</param>
public sealed record FremdesProfilAbfrage(SubjectId Wer, TenantId Firma) : IAbfrage<Profil?>;

/// <summary>Liest ein fremdes Profil, wenn der Ledger es erlaubt.</summary>
/// <remarks>
/// <c>null</c> heißt „nicht vorhanden ODER nicht freigegeben“ — und die beiden
/// müssen von außen ununterscheidbar bleiben. Ein eigener Ausgang für
/// „existiert, zeigt sich aber nicht“ wäre ein Orakel: wer eine Liste von
/// Kennungen durchprobiert, erführe, wer Mitglied ist, ohne je ein Profil zu
/// sehen (ADR-0020 §1).
/// </remarks>
public sealed class FremdesProfilHandler(IProfilspeicher speicher, IEinwilligungstor tor)
    : IRequestHandler<FremdesProfilAbfrage, Profil?>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<Profil?> Handle(
        FremdesProfilAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profil = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (profil is null)
        {
            // Kein Ledger-Aufruf für ein Profil, das es nicht gibt: ein
            // überflüssiger Umlauf, und er meldete dem Ledger geratene
            // Kennungen.
            return null;
        }

        // EinwilligungSchweigt fliegt bewusst durch: der Endpunkt macht daraus
        // 503. Es hier zu false zu machen hieße zu behaupten, die Person habe
        // nicht eingewilligt — das wissen wir nicht.
        return await tor.DarfSehenAsync(request.Wer, request.Firma, cancellationToken)
            ? profil
            : null;
    }
}
