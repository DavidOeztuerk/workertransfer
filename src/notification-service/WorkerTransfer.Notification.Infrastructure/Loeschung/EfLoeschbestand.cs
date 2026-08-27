using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Infrastructure.Persistence;

namespace WorkerTransfer.Notification.Infrastructure.Loeschung;

/// <summary>Was eine Löschung in diesem Dienst anfasst.</summary>
/// <remarks>
/// Zwei Anweisungen, beide ohne Ausnahme. Es gibt hier keine Zeile, in der
/// jemand nur <em>gehandelt</em> hätte — jede handelt <em>von</em> der Person,
/// und deshalb fällt jede. Und keinen Aufbewahrungsschalter: es wurde nie
/// behauptet, dass hier etwas einer Aufbewahrungspflicht unterläge.
/// </remarks>
public sealed class EfLoeschbestand(NotificationDbContext kontext) : ILoeschbestand
{
    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        await kontext.Eingaenge
            .Where(zeile => zeile.UserId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await kontext.Wuensche
            .Where(zeile => zeile.Id == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Nichts bleibt. Die Quittung sagt es, statt den Ursprung raten zu
        // lassen (ADR-0027 §3).
        return 0;
    }
}
