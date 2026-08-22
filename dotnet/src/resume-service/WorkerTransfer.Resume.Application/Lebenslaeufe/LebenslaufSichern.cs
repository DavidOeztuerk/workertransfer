using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>Write or rewrite one's own résumé.</summary>
/// <remarks>
/// Owner only. There is no command that writes somebody else's résumé, and the
/// subject is not in the body — it comes from the verified token, so a caller
/// cannot name a different one.
/// </remarks>
public sealed record LebenslaufSichernBefehl(
    SubjectId Wer,
    IReadOnlyList<Station> Stationen,
    IReadOnlyList<Ausbildung> Ausbildungen) : IBefehl<Lebenslauf>;

/// <summary>Creates it the first time and replaces it afterwards.</summary>
public sealed class LebenslaufSichernHandler(
    ILebenslaufSpeicher speicher,
    IPruefspur pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<LebenslaufSichernBefehl, Lebenslauf>
{
    /// <inheritdoc />
    public async Task<Lebenslauf> Handle(
        LebenslaufSichernBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();
        var vorhanden = await speicher.HoleAsync(request.Wer, cancellationToken);

        Lebenslauf lebenslauf;

        if (vorhanden is null)
        {
            lebenslauf = Lebenslauf.Anlegen(
                request.Wer, request.Stationen, request.Ausbildungen, jetzt);
        }
        else
        {
            vorhanden.Aktualisiere(request.Stationen, request.Ausbildungen, jetzt);
            lebenslauf = vorhanden;
        }

        await speicher.SichereAsync(lebenslauf, cancellationToken);

        await pruefspur.AppendAsync(
            anhang.Eintrag(Pruefhandlung.ResumeSaved, jetzt, request.Wer, betroffene: request.Wer),
            cancellationToken);

        return lebenslauf;
    }
}
