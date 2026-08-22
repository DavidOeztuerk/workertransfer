using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>The person answers one company's question.</summary>
public sealed record AnfrageBeantwortenBefehl(Guid Anfrage, SubjectId Akteur, bool Erteilen)
    : IBefehl<Anfrageergebnis>;

/// <summary>Writes the ledger first and the proceeding second.</summary>
/// <remarks>
/// The order is load-bearing. The ledger call happens inside the transaction;
/// if it fails, <see cref="EinwilligungSchweigt"/> travels out and nothing is
/// committed. The other way round could leave a proceeding at
/// <see cref="Anfragestand.Granted"/> with no permission behind it.
/// <para>
/// That leaves one gap: the ledger call succeeds and the commit then fails, so
/// the permission exists while the proceeding stays pending. Which is why a
/// <em>refusal</em> withdraws as well. A "no" then does not mean "nothing was
/// granted" but "this company holds nothing", and every answer leads the system
/// back into a safe state. The ledger takes a withdrawal without a preceding
/// grant: it appends a REVOKE event, and the answer afterwards reads as it did
/// before.
/// </para>
/// </remarks>
public sealed class AnfrageBeantwortenHandler(
    IAnfragenSpeicher speicher,
    IEinwilligungstor tor,
    IOutbox outbox,
    IPruefspur pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<AnfrageBeantwortenBefehl, Anfrageergebnis>
{
    /// <inheritdoc />
    public async Task<Anfrageergebnis> Handle(
        AnfrageBeantwortenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anfrage = await speicher.HoleAsync(request.Anfrage, cancellationToken);

        // A foreign request id behaves like a foreign subject id: not there and
        // not mine are the same thing from outside.
        if (anfrage is null || anfrage.Wer != request.Akteur)
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        var jetzt = uhr.GetUtcNow();

        try
        {
            if (request.Erteilen)
            {
                anfrage.Erteile(request.Akteur, jetzt);
            }
            else
            {
                anfrage.LehneAb(request.Akteur, jetzt);
            }
        }
        catch (Anfrageregel regel)
        {
            return new Anfrageergebnis.Regelverstoss(regel.Code, regel.Message);
        }

        if (request.Erteilen)
        {
            await tor.ErteileAsync(anfrage.Wer, anfrage.Firma, cancellationToken);
        }
        else
        {
            await tor.WiderrufeAsync(anfrage.Wer, anfrage.Firma, cancellationToken);
        }

        await speicher.SichereAsync(anfrage, cancellationToken);

        // To whoever asked, not to the company as such: an outbox row names one
        // person. `null` means that account is gone, and then there is nobody
        // to tell.
        if (anfrage.Frager is { } frager)
        {
            await outbox.VermerkeAsync(
                frager,
                request.Erteilen ? Benachrichtigungsarten.Erteilt : Benachrichtigungsarten.Abgelehnt,
                cancellationToken);
        }

        await pruefspur.AppendAsync(
            anhang.Eintrag(
                request.Erteilen ? Pruefhandlung.RequestGranted : Pruefhandlung.RequestDeclined,
                jetzt, akteur: anfrage.Wer, firma: anfrage.Firma, betroffene: anfrage.Wer),
            cancellationToken);

        return new Anfrageergebnis.Erledigt(anfrage);
    }
}
