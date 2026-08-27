using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Outbox;

/// <summary>One outstanding intent, as the dispatcher sees it.</summary>
public sealed record OutboxEintrag(Guid Id, SubjectId Empfaenger, string Art, int Versuche);

/// <summary>How often a row may fail before it is left lying.</summary>
public static class Zustellgrenzen
{
    /// <summary>
    /// Ten, for a notification.
    /// </summary>
    /// <remarks>
    /// Giving up is not deleting: the row stays and stays queryable. A row that
    /// vanishes quietly is exactly the state this table abolishes.
    /// </remarks>
    public const int Standard = 10;
}

/// <summary>Takes outstanding rows and delivers them.</summary>
/// <remarks>
/// Runs <em>inside the service</em> as a background task, not as a process of
/// its own: another service would be another deployment, another health check
/// and another place to forget — for a loop that reads one table.
/// </remarks>
/// <typeparam name="TKontext">The service's own context.</typeparam>
public sealed class OutboxZusteller<TKontext>(
    TKontext kontext,
    IZustellung zustellung,
    TimeProvider uhr,
    ILogger<OutboxZusteller<TKontext>> protokoll,
    OutboxEinstellungen? einstellungen = null)
    where TKontext : DbContext
{
    private readonly OutboxEinstellungen _einstellungen = einstellungen ?? new OutboxEinstellungen();

    /// <summary>One pass. Answers how many were due and how many really went out.</summary>
    /// <remarks>
    /// Two numbers, not one. The second alone cannot tell "nothing to do" from
    /// "nothing got through" — and whoever paces the loop by it slows a quiet
    /// service exactly like a blocked one. That is not hypothetical: on a system
    /// with no erasures the Python dispatcher sat at its ceiling after minutes,
    /// and the next erasure did not start for minutes.
    /// </remarks>
    public async Task<(int Faellig, int Zugestellt)> DurchlaufAsync(
        CancellationToken cancellationToken = default)
    {
        // The rows are locked for the length of this transaction, so a second
        // dispatcher skips them instead of delivering the same intent twice.
        // Python has no such lock, which is one of three reasons its Helm chart
        // is pinned to one replica.
        await using var klammer = await kontext.Database.BeginTransactionAsync(cancellationToken);

        var faellige = await FaelligeAsync(cancellationToken);
        var zugestellt = 0;

        foreach (var zeile in faellige)
        {
            if (await ZustelleAsync(zeile, cancellationToken))
            {
                zugestellt++;
            }
        }

        await kontext.SaveChangesAsync(cancellationToken);
        await klammer.CommitAsync(cancellationToken);

        return (faellige.Count, zugestellt);
    }

    /// <summary>
    /// The outstanding rows, locked against every other dispatcher.
    /// </summary>
    /// <remarks>
    /// Raw SQL, because <c>FOR UPDATE SKIP LOCKED</c> has no expression in LINQ
    /// and is the whole reason more than one replica is safe. Postgres-specific,
    /// deliberately: every service here runs Postgres, and a portable version
    /// would have to give up the guarantee.
    /// </remarks>
    private async Task<IReadOnlyList<OutboxZeile>> FaelligeAsync(
        CancellationToken cancellationToken)
    {
        // The table name is a setting, not caller input, and it is quoted — but
        // the two real parameters go in as parameters, because one day somebody
        // will make the batch size configurable from somewhere else.
        var sql = "SELECT * FROM \"" + _einstellungen.Tabelle + "\" "
                  + "WHERE delivered_at IS NULL "
                  // `null` filters nothing: the row stays due however often it
                  // has failed. That is what "never give up" means in SQL.
                  + "AND ({0}::int IS NULL OR attempts < {0}::int) "
                  // Oldest first: a notification that gets overtaken arrives in
                  // the wrong order.
                  + "ORDER BY created_at LIMIT {1} "
                  // The whole reason more than one replica is safe.
                  + "FOR UPDATE SKIP LOCKED";

        object grenze = _einstellungen.HoechsteVersuche is { } wert ? wert : DBNull.Value;

        // Tracked, and the rows are handed on as they are. Reading them a
        // second time would hand back untracked copies wherever the service
        // configured NoTracking — and then every "delivered" would be written
        // onto an object nobody saves. That is not hypothetical: it is how this
        // method used to work.
        return await kontext.Set<OutboxZeile>()
            .FromSqlRaw(sql, grenze, _einstellungen.Stapelgroesse)
            .AsTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> ZustelleAsync(
        OutboxZeile zeile,
        CancellationToken cancellationToken)
    {
        try
        {
            await zustellung.ZustelleAsync(
                new SubjectId(zeile.UserId), zeile.Kind, cancellationToken);
        }
        catch (NochNichtException)
        {
            // No attempt, no error, no trace: the row was not due. Offering it
            // again on the next tick IS how the order of ADR-0027 §6 is kept.
            return false;
        }
        catch (Exception fehler)
        {
            // Only the kind, never the content — and shortened, so a talkative
            // failure cannot burst the column.
            zeile.Attempts += 1;
            zeile.LastError = Gekuerzt(fehler.GetType().Name);

            protokoll.LogWarning(
                "Zustellung fehlgeschlagen ({Art}, Versuch {Versuch})",
                fehler.GetType().Name, zeile.Attempts);

            return false;
        }

        zeile.Attempts += 1;
        zeile.DeliveredAt = uhr.GetUtcNow().UtcDateTime;

        return true;
    }

    private static string Gekuerzt(string wert) => wert.Length <= 120 ? wert : wert[..120];
}
