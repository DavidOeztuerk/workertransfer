using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Application.Bewerbungen;

/// <summary>Zurückziehen — der Vorgang bleibt, die Daten sind weg.</summary>
/// <remarks>
/// Dass jemand sich beworben und zurückgezogen hat, gehört zur Geschichte des
/// Verfahrens im Unternehmen. Die Person dahinter ist danach nicht mehr
/// einsehbar.
/// </remarks>
public sealed record BewerbungZurueckziehenBefehl(Guid Id, SubjectId Wer)
    : IBefehl<Bewerbungsergebnis>;

/// <summary>Setzt den Stand und widerruft die Freigaben.</summary>
public sealed class BewerbungZurueckziehenHandler(
    IBewerbungsspeicher speicher,
    IEinwilligungsschreiber ledger,
    TimeProvider uhr) : IRequestHandler<BewerbungZurueckziehenBefehl, Bewerbungsergebnis>
{
    /// <inheritdoc />
    public async Task<Bewerbungsergebnis> Handle(
        BewerbungZurueckziehenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bewerbung = await speicher.HoleAsync(request.Id, cancellationToken);

        // Nicht vorhanden und nicht meins sind von außen dasselbe.
        if (bewerbung is null || bewerbung.Wer != request.Wer)
        {
            return new Bewerbungsergebnis.Unbekannt();
        }

        try
        {
            bewerbung.Ziehe_zurueck(request.Wer, uhr.GetUtcNow());
        }
        catch (UebergangNichtErlaubt fehler)
        {
            return new Bewerbungsergebnis.Zustandskonflikt(fehler.Message);
        }

        // Bedingungslos widerrufen — auch, was vielleicht gar nicht erteilt
        // wurde. Der Ledger verträgt einen Widerruf ohne vorherige Erteilung,
        // und die Alternative wäre, sich auf die eigene Zeile zu verlassen, um
        // zu wissen, was im Ledger steht.
        await ledger.WiderrufeAsync(
            request.Wer, Einwilligungsschluessel.Alles(bewerbung.Firma), cancellationToken);

        await speicher.SichereAsync(bewerbung, cancellationToken);

        return new Bewerbungsergebnis.Erledigt(bewerbung);
    }
}
