using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Das PersonProfile eines anderen Menschen.</summary>
/// <param name="Wer">Wessen PersonProfile gesucht wird.</param>
/// <param name="Firma">Das Unternehmen des Aufrufers — aus dem Token, nie aus der ResumeRequest.</param>
public sealed record ForeignProfileQuery(SubjectId Wer, TenantId Firma) : IAbfrage<PersonProfile?>;

/// <summary>Liest ein fremdes PersonProfile, wenn der Ledger es erlaubt.</summary>
/// <remarks>
/// <c>null</c> heißt „nicht vorhanden ODER nicht freigegeben“ — und die beiden
/// müssen von außen ununterscheidbar bleiben. Ein eigener Ausgang für
/// „existiert, zeigt sich aber nicht“ wäre ein Orakel: wer eine Liste von
/// Kennungen durchprobiert, erführe, wer Mitglied ist, ohne je ein PersonProfile zu
/// sehen (ADR-0020 §1).
/// </remarks>
public sealed class ForeignProfileHandler(IProfileStore store, IConsentGate tor)
    : IRequestHandler<ForeignProfileQuery, PersonProfile?>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<PersonProfile?> Handle(
        ForeignProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await store.GetAsync(request.Wer, cancellationToken);

        if (profile is null)
        {
            // Kein Ledger-Aufruf für ein PersonProfile, das es nicht gibt: ein
            // überflüssiger Umlauf, und er meldete dem Ledger geratene
            // Kennungen.
            return null;
        }

        // ConsentGateSilent fliegt bewusst durch: der Endpunkt macht daraus
        // 503. Es hier zu false zu machen hieße zu behaupten, die Person habe
        // nicht eingewilligt — das wissen wir nicht.
        return await tor.MaySeeAsync(request.Wer, request.Firma, cancellationToken)
            ? profile
            : null;
    }
}
