using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Outbox;

/// <summary>The loop that keeps the table moving.</summary>
/// <remarks>
/// The library holds the table and "record the intent in the same transaction";
/// the loop belongs to the service. Girder's own rule — it ships
/// <c>PurgeAsync</c> and never calls it, because a background loop inside a
/// library is a loop nobody can see.
/// <para>
/// It must never die. If it dies, the table sits there and nobody notices —
/// exactly the state before the outbox existed.
/// </para>
/// </remarks>
/// <typeparam name="TKontext">The service's own context.</typeparam>
public sealed class OutboxSchleife<TKontext>(
    IServiceScopeFactory bereiche,
    OutboxEinstellungen einstellungen,
    TimeProvider uhr,
    ILogger<OutboxSchleife<TKontext>> protokoll) : BackgroundService
    where TKontext : DbContext
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var takt = einstellungen.Takt;

        while (!stoppingToken.IsCancellationRequested)
        {
            int faellig;
            int zugestellt;

            try
            {
                // A scope per pass: the dispatcher and its delivery reach into
                // a database, and a background loop that holds one context for
                // its whole life holds it for days.
                using var bereich = bereiche.CreateScope();

                (faellig, zugestellt) = await bereich.ServiceProvider
                    .GetRequiredService<OutboxZusteller<TKontext>>()
                    .DurchlaufAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception fehler)
            {
                protokoll.LogWarning(fehler, "Outbox-Durchlauf fehlgeschlagen");
                (faellig, zugestellt) = (1, 0);
            }

            // Blocked means: there was something to do and none of it got
            // through. Quiet means: nothing was due — and a quiet service must
            // not be slowed down, or the next erasure pays for the last one
            // having waited on a dead recipient.
            var blockiert = faellig > 0 && zugestellt == 0;

            takt = blockiert
                ? Kleiner(takt * 2, einstellungen.HoechsterTakt)
                : einstellungen.Takt;

            try
            {
                await Task.Delay(takt, uhr, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static TimeSpan Kleiner(TimeSpan links, TimeSpan rechts) =>
        links < rechts ? links : rechts;
}
