using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>The person takes a release back.</summary>
public sealed record ZugriffWiderrufenBefehl(Guid Anfrage, SubjectId Akteur)
    : IBefehl<Anfrageergebnis>;

/// <summary>Works in the ledger, and leaves the proceeding untouched.</summary>
/// <remarks>
/// <see cref="Anfragestand.Granted"/> means "was granted once". Changing it on a
/// withdrawal would rewrite history — and whether access holds is answered by
/// the ledger anyway. This is the single place where somebody reading the code
/// is most tempted to "fix" a bug that is the design.
/// </remarks>
public sealed class ZugriffWiderrufenHandler(
    IAnfragenSpeicher speicher,
    IEinwilligungstor tor,
    IPruefspur pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<ZugriffWiderrufenBefehl, Anfrageergebnis>
{
    /// <inheritdoc />
    public async Task<Anfrageergebnis> Handle(
        ZugriffWiderrufenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anfrage = await speicher.HoleAsync(request.Anfrage, cancellationToken);

        if (anfrage is null || anfrage.Wer != request.Akteur)
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        await tor.WiderrufeAsync(anfrage.Wer, anfrage.Firma, cancellationToken);

        await pruefspur.AppendAsync(
            anhang.Eintrag(
                Pruefhandlung.AccessRevoked, uhr.GetUtcNow(),
                akteur: anfrage.Wer, firma: anfrage.Firma, betroffene: anfrage.Wer),
            cancellationToken);

        return new Anfrageergebnis.Erledigt(anfrage);
    }
}
