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
/// <strong>Nur das eigene Konto, und deshalb steht keine Kennung im Rumpf.</strong>
/// In das Konto eines anderen zu schreiben ist keine Fähigkeit, die dieser
/// Dienst braucht, und eine Kennung wäre das Einzige, was zwischen einem
/// Aufrufer und genau dem stünde.
/// <para>
/// <strong><c>null</c> heisst entfernen.</strong> Ohne diesen Weg wäre eine
/// einmal getroffene Wahl endgültig, und wer sich beim Anmelden vertan hat,
/// bliebe für immer Metallbauer. Ein Befehl, der nur setzen kann, sieht
/// harmloser aus, als er ist.
/// </para>
/// <para>
/// Ein Befehl und keine Ableitung: das Berufsfeld wird nie aus dem Verhalten
/// geschlossen (ADR-0039). Wer eine GitHub-Verbindung hat, wird dadurch nicht
/// <c>it_software</c> — das wäre eine abgeleitete Eigenschaft über einen
/// Menschen, gebildet ohne ihn und unsichtbar für ihn.
/// </para>
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

        // Aggregate kommen losgelöst aus dem Speicher zurück: ohne diesen Aufruf
        // erreicht die Wahl die Datenbank nie. Im Test kostet das Vergessen
        // nichts und in Betrieb die Eingabe.
        await benutzer.SaveAsync(konto, cancellationToken);

        return true;
    }
}
