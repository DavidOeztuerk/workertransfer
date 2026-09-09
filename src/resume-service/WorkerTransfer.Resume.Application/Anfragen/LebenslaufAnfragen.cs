using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>A company asks a person for their résumé.</summary>
public sealed record LebenslaufAnfragenBefehl(SubjectId Wer, TenantId Firma, SubjectId Frager)
    : IBefehl<Anfrageergebnis>;

/// <summary>
/// Opens the proceeding — if the person's profile is released.
/// </summary>
/// <remarks>
/// The precondition is the <em>profile</em> release, never the existence of a
/// résumé. Checking both would be an oracle: "has already written a CV" is a
/// fact about the person that nobody should be able to probe for, and a company
/// that got a different answer for a person with a résumé than for one without
/// would have learned it without asking.
/// </remarks>
public sealed class LebenslaufAnfragenHandler(
    IAnfragenSpeicher speicher,
    IEinwilligungstor tor,
    IOutbox outbox,
    IPruefspur pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<LebenslaufAnfragenBefehl, Anfrageergebnis>
{
    /// <inheritdoc />
    public async Task<Anfrageergebnis> Handle(
        LebenslaufAnfragenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await tor.DarfProfilSehenAsync(request.Wer, cancellationToken))
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        if (await speicher.FindeAsync(request.Wer, request.Firma, cancellationToken) is not null)
        {
            return new Anfrageergebnis.SchonGefragt();
        }

        var jetzt = uhr.GetUtcNow();
        var anfrage = Anfrage.Oeffne(request.Wer, request.Firma, request.Frager, jetzt);

        await speicher.FuegeHinzuAsync(anfrage, cancellationToken);

        // In the SAME transaction as the request (ADR-0025). If the request
        // gets through, the notification is settled; if it rolls back, nobody
        // is told a company asked when none did.
        await outbox.VermerkeAsync(request.Wer, Benachrichtigungsarten.Angefragt, cancellationToken);

        await pruefspur.AppendAsync(
            anhang.Eintrag(
                Pruefhandlung.ResumeRequested, jetzt,
                akteur: request.Frager, firma: request.Firma, betroffene: request.Wer),
            cancellationToken);

        return new Anfrageergebnis.Erledigt(anfrage);
    }
}
