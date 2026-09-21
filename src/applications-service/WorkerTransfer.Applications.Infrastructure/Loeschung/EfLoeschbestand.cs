using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Erasure;

/// <summary>Was eine Löschung in diesem Dienst wirklich anfasst.</summary>
/// <remarks>
/// Zwei Anweisungen, und die erste ist die, auf die es ankommt: in der
/// Voreinstellung fallen <em>alle</em> Applications dieses Menschen, auch die
/// eingestellten und die bezahlten Vorgänge dahinter. Nur der umgelegte
/// Schalter nimmt genau eine Zeilenklasse aus.
/// </remarks>
public sealed class EfErasureLedger(ApplicationsDbContext context) : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject,
        bool eingestellteBehalten,
        CancellationToken cancellationToken = default)
    {
        var eingestellt = ApplicationStates.Wort(ApplicationState.Hired);
        var meine = context.Applications.Where(row => row.SubjectId == subject.Value);

        var behalten = eingestellteBehalten
            ? await meine.CountAsync(row => row.Status == eingestellt, cancellationToken)
            : 0;

        // Ausgeschrieben statt als Ternär im Ausdruck: das ist die Zeile, die
        // entscheidet, ob eine eingestellte JobApplication fällt. Wer sie
        // überfliegt, soll sehen, was sie tut — nicht Operatorvorrang
        // nachschlagen müssen.
        if (eingestellteBehalten)
        {
            await meine
                .Where(row => row.Status != eingestellt)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await meine.ExecuteDeleteAsync(cancellationToken);
        }

        // Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.
        await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Ausgesetzt ist nicht übersprungen: der Ursprung soll erfahren, was
        // stehen blieb, statt es zu vermuten (ADR-0027 §3.4).
        return behalten;
    }
}
