using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Loeschung;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Vorgaenge;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

namespace WorkerTransfer.Transfer.Infrastructure.Loeschung;

/// <summary>Was eine Löschung in diesem Dienst wirklich anfasst.</summary>
/// <remarks>
/// Fünf Anweisungen, und die Paarung der mittleren beiden ist der ganze
/// Entwurf: eine Zeile <em>über</em> die Person fällt, eine Zeile, in der die
/// Person nur <em>gehandelt</em> hat, bleibt ohne ihren Namen stehen. Ohne
/// diese Unterscheidung löschte ein Recruiter, der sein privates Konto aufgibt,
/// Vorgänge seines Arbeitgebers, die von jemand ganz anderem handeln.
/// </remarks>
public sealed class EfLoeschbestand(TransferDbContext kontext) : ILoeschbestand
{
    private static readonly string[] Abgeschlossene =
        [.. Aufbewahrung.Abgeschlossene.Select(Transferstaende.Wort)];

    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer,
        bool bezahlteBehalten,
        CancellationToken cancellationToken = default)
    {
        // Der Marktstatus — die gefährlichste Angabe im System, samt Notiz.
        await kontext.Marktstatus
            .Where(zeile => zeile.Id == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Was ÜBER die Person gesagt wurde, fällt: die Zeile IST die Aussage
        // „Unternehmen X hat nach diesem Menschen gefragt".
        await kontext.Anfragen
            .Where(zeile => zeile.SubjectId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Was die Person FÜR ihr Unternehmen tat, bleibt — ohne ihren Namen.
        // Die Anfrage gehört dem Unternehmen und handelt von einem Dritten.
        await kontext.Anfragen
            .Where(zeile => zeile.RequestedBy == wer.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(zeile => zeile.RequestedBy, (Guid?)null),
                cancellationToken);

        var meine = kontext.Vorgaenge.Where(zeile => zeile.SubjectId == wer.Value);

        var behalten = bezahlteBehalten
            ? await meine.CountAsync(Bezahlt(), cancellationToken)
            : 0;

        // Ausgeschrieben statt als Ternär im Ausdruck: das ist die Zeile, die
        // entscheidet, ob ein bezahlter Transfer fällt. Wer sie überfliegt,
        // soll sehen, was sie tut — nicht Operatorvorrang nachschlagen müssen.
        if (bezahlteBehalten)
        {
            await meine
                .Where(zeile => !(Abgeschlossene.Contains(zeile.Status)
                                  && zeile.OfferFeeCents != null))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await meine.ExecuteDeleteAsync(cancellationToken);
        }

        // Ein ausstehender Vermerk an ein Konto, das es nicht mehr gibt.
        await kontext.Set<OutboxZeile>()
            .Where(zeile => zeile.UserId == wer.Value)
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
    private static System.Linq.Expressions.Expression<Func<VorgangsZeile, bool>> Bezahlt() =>
        zeile => Abgeschlossene.Contains(zeile.Status) && zeile.OfferFeeCents != null;
}
