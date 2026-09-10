using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Eine Person nennt ihr Berufsfeld — oder nimmt die Angabe zurück.</summary>
/// <param name="Wer">Wessen Konto. Immer das des Aufrufers.</param>
/// <param name="Berufsfeld">Was sie gewählt hat, oder <c>null</c> für „keins".</param>
public sealed record BerufsfeldWaehlenBefehl(SubjectId Wer, Berufsfeld? Berufsfeld)
    : IBefehl<bool>;

/// <inheritdoc cref="BerufsfeldWaehlenBefehl" />
/// <remarks>
/// Nur das eigene Konto — deshalb steht keine Kennung im Rumpf. <c>null</c>
/// heisst entfernen, sonst wäre eine einmal getroffene Wahl endgültig. Das
/// Berufsfeld wird nie aus dem Verhalten geschlossen (ADR-0039).
/// </remarks>
public sealed class BerufsfeldWaehlenHandler(IUserRepository benutzer)
    : IRequestHandler<BerufsfeldWaehlenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        BerufsfeldWaehlenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken);

        if (konto is null)
        {
            return false;
        }

        konto.BerufsfeldWaehlen(request.Berufsfeld);

        // Aggregate kommen losgelöst zurück: ohne diesen Aufruf erreicht die
        // Wahl die Datenbank nie.
        await benutzer.SaveAsync(konto, cancellationToken);

        return true;
    }
}
