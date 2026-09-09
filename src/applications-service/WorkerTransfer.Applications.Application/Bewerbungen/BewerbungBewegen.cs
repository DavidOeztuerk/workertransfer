using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Application.Bewerbungen;

/// <summary>Das Unternehmen bewegt die Bewerbung durch das Verfahren.</summary>
public sealed record BewerbungBewegenBefehl(Guid Id, TenantId Firma, string Stand)
    : IBefehl<Bewerbungsergebnis>;

/// <summary>Setzt den Stand und vermerkt die Benachrichtigung.</summary>
public sealed class BewerbungBewegenHandler(
    IBewerbungsspeicher speicher,
    IOutbox outbox,
    TimeProvider uhr) : IRequestHandler<BewerbungBewegenBefehl, Bewerbungsergebnis>
{
    /// <inheritdoc />
    public async Task<Bewerbungsergebnis> Handle(
        BewerbungBewegenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bewerbung = await speicher.HoleAsync(request.Id, cancellationToken);

        // Eine fremde Bewerbung ist von außen wie keine.
        if (bewerbung is null || bewerbung.Firma != request.Firma)
        {
            return new Bewerbungsergebnis.Unbekannt();
        }

        // Ein Standwort, das es nicht gibt, lässt der Vertrag nicht durch —
        // aber ein Handler soll auch ohne ihn nicht abstürzen.
        if (!Enum.TryParse<Bewerbungsstand>(request.Stand, ignoreCase: true, out var gewollt)
            || !Enum.IsDefined(gewollt))
        {
            return new Bewerbungsergebnis.Eingabe(
                "That is not a status an application can have");
        }

        try
        {
            bewerbung.Bewege(gewollt, uhr.GetUtcNow());
        }
        catch (UebergangNichtErlaubt fehler)
        {
            return new Bewerbungsergebnis.Zustandskonflikt(fehler.Message);
        }

        await speicher.SichereAsync(bewerbung, cancellationToken);

        // In DERSELBEN Transaktion wie der Zug (ADR-0025). Vorher stand hier
        // ein HTTP-Aufruf nach dem Commit, dessen Fehler geschluckt wurde — die
        // Zusage „eine misslungene Mail darf den Vorgang nicht kippen" galt,
        // aber der Preis war, dass die Nachricht dann für immer weg war.
        await outbox.VermerkeAsync(bewerbung.Wer, Benachrichtigungsarten.Bewegt, cancellationToken);

        return new Bewerbungsergebnis.Erledigt(bewerbung);
    }
}
