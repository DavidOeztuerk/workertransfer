using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Outbox;

/// <summary>The loop that keeps the table moving.</summary>
/// <remarks>
/// The library holds the table and "record the intent in the same transaction";
/// the loop belongs to the service. Noelia's own rule — it ships
/// <c>PurgeAsync</c> and never calls it, because a background loop inside a
/// library is a loop nobody can see.
/// <para>
/// It must never die. If it dies, the table sits there and nobody notices —
/// exactly the state before the outbox existed.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The service's own context.</typeparam>
public sealed class OutboxLoop<TContext>(
    IServiceScopeFactory scopes,
    OutboxSettings settings,
    TimeProvider clock,
    ILogger<OutboxLoop<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = settings.Takt;

        while (!stoppingToken.IsCancellationRequested)
        {
            int due;
            int delivered;

            try
            {
                // A scope per pass: the dispatcher and its delivery reach into
                // a database, and a background loop that holds one context for
                // its whole life holds it for days.
                using var scope = scopes.CreateScope();

                (due, delivered) = await scope.ServiceProvider
                    .GetRequiredService<OutboxDispatcher<TContext>>()
                    .PassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Outbox pass failed");
                (due, delivered) = (1, 0);
            }

            // Blocked means: there was something to do and none of it got
            // through. Quiet means: nothing was due — and a quiet service must
            // not be slowed down, or the next erasure pays for the last one
            // having waited on a dead recipient.
            var blocked = due > 0 && delivered == 0;

            interval = blocked
                ? Smaller(interval * 2, settings.HoechsterTakt)
                : settings.Takt;

            try
            {
                await Task.Delay(interval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static TimeSpan Smaller(TimeSpan left, TimeSpan right) =>
        left < right ? left : right;
}
