using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Loeschung;

/// <summary>Was eine Löschung in diesem Dienst wirklich anfasst.</summary>
/// <remarks>
/// Zwei Anweisungen, und die erste ist die, auf die es ankommt: in der
/// Voreinstellung fallen <em>alle</em> Bewerbungen dieses Menschen, auch die
/// eingestellten und die bezahlten Vorgänge dahinter. Nur der umgelegte
/// Schalter nimmt genau eine Zeilenklasse aus.
/// </remarks>
public sealed class EfLoeschbestand(ApplicationsDbContext kontext) : ILoeschbestand
{
    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer,
        bool eingestellteBehalten,
        CancellationToken cancellationToken = default)
    {
        var eingestellt = Bewerbungsstaende.Wort(Bewerbungsstand.Hired);
        var meine = kontext.Bewerbungen.Where(zeile => zeile.SubjectId == wer.Value);

        var behalten = eingestellteBehalten
            ? await meine.CountAsync(zeile => zeile.Status == eingestellt, cancellationToken)
            : 0;

        // Ausgeschrieben statt als Ternär im Ausdruck: das ist die Zeile, die
        // entscheidet, ob eine eingestellte Bewerbung fällt. Wer sie
        // überfliegt, soll sehen, was sie tut — nicht Operatorvorrang
        // nachschlagen müssen.
        if (eingestellteBehalten)
        {
            await meine
                .Where(zeile => zeile.Status != eingestellt)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await meine.ExecuteDeleteAsync(cancellationToken);
        }

        // Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.
        await kontext.Set<OutboxZeile>()
            .Where(zeile => zeile.UserId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Ausgesetzt ist nicht übersprungen: der Ursprung soll erfahren, was
        // stehen blieb, statt es zu vermuten (ADR-0027 §3.4).
        return behalten;
    }
}
