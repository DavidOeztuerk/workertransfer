using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Infrastructure.Persistence;

namespace WorkerTransfer.Notification.Infrastructure.Erasure;

/// <summary>Was eine Löschung in diesem Dienst anfasst.</summary>
/// <remarks>
/// Zwei Anweisungen, beide ohne Ausnahme. Es gibt hier keine Zeile, in der
/// jemand nur <em>gehandelt</em> hätte — jede handelt <em>von</em> der Person,
/// und deshalb fällt jede. Und keinen Aufbewahrungsschalter: es wurde nie
/// behauptet, dass hier etwas einer Aufbewahrungspflicht unterläge.
/// </remarks>
public sealed class EfErasureLedger(NotificationDbContext context) : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        await context.Eingaenge
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Wuensche
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Nichts bleibt. Die Quittung sagt es, statt den Ursprung raten zu
        // lassen (ADR-0027 §3).
        return 0;
    }
}
