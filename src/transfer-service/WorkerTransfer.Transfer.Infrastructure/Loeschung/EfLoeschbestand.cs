using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Erasure;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Vorgaenge;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

namespace WorkerTransfer.Transfer.Infrastructure.Erasure;

/// <summary>Was eine Löschung in diesem Dienst wirklich anfasst.</summary>
/// <remarks>
/// Fünf Anweisungen, und die Paarung der mittleren beiden ist der ganze
/// Entwurf: eine Zeile <em>über</em> die Person fällt, eine Zeile, in der die
/// Person nur <em>gehandelt</em> hat, bleibt ohne ihren Namen stehen. Ohne
/// diese Unterscheidung löschte ein Recruiter, der sein privates Konto aufgibt,
/// Vorgänge seines Arbeitgebers, die von jemand ganz anderem handeln.
/// </remarks>
public sealed class EfErasureLedger(TransferDbContext context) : IErasureLedger
{
    private static readonly string[] Abgeschlossene =
        [.. Retention.Abgeschlossene.Select(Transferstaende.Wort)];

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject,
        bool bezahlteBehalten,
        CancellationToken cancellationToken = default)
    {
        // Der MarketStatus — die gefährlichste Angabe im System, samt Notiz.
        await context.MarketStatus
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Was ÜBER die Person gesagt wurde, fällt: die Zeile IST die Aussage
        // „Unternehmen X hat nach diesem Menschen gefragt".
        await context.Anfragen
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Was die Person FÜR ihr Unternehmen tat, bleibt — ohne ihren Namen.
        // Die ResumeRequest gehört dem Unternehmen und handelt von einem Dritten.
        await context.Anfragen
            .Where(row => row.RequestedBy == subject.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(row => row.RequestedBy, (Guid?)null),
                cancellationToken);

        var meine = context.Vorgaenge.Where(row => row.SubjectId == subject.Value);

        var behalten = bezahlteBehalten
            ? await meine.CountAsync(Bezahlt(), cancellationToken)
            : 0;

        // Ausgeschrieben statt als Ternär im Ausdruck: das ist die Zeile, die
        // entscheidet, ob ein bezahlter Transfer fällt. Wer sie überfliegt,
        // soll sehen, was sie tut — nicht Operatorvorrang nachschlagen müssen.
        if (bezahlteBehalten)
        {
            await meine
                .Where(row => !(Abgeschlossene.Contains(row.Status)
                                  && row.OfferFeeCents != null))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await meine.ExecuteDeleteAsync(cancellationToken);
        }

        // Ein ausstehender Vermerk an ein Konto, das es nicht mehr gibt.
        await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Ausgesetzt ist nicht übersprungen: der Ursprung soll erfahren, was
        // stehen blieb, statt es zu vermuten (ADR-0027 §3.4).
        return behalten;
    }

    /// <summary>Ein abgeschlossener Handel <em>mit</em> Vergütung.</summary>
    /// <remarks>
    /// Ohne Vergütung ist kein Handelsvorgang entstanden, an dem etwas hängen
    /// könnte — und ein Gespräch ist kein Vertrag.
    /// </remarks>
    private static System.Linq.Expressions.Expression<Func<CaseRow, bool>> Bezahlt() =>
        row => Abgeschlossene.Contains(row.Status) && row.OfferFeeCents != null;
}
