using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Migrate first, then serve.</summary>
/// <remarks>
/// Every service owns its own schema (ADR-0010) and migrates only itself —
/// there is no central step that would have to know all ten. That is what makes
/// <c>docker compose up</c> enough on a fresh clone: no script, no <c>psql</c>
/// by hand, no order to remember.
/// <para>
/// <strong>At startup and not in a container of its own</strong>, unlike in the
/// Python era: <c>dotnet ef</c> needs the SDK, and a runtime image has none.
/// The price is a fourth reason for <c>replicaCount: 1</c> — two pilgrims on
/// the same schema are a race nobody wins. Whoever raises the number has to
/// come back here first.
/// </para>
/// <para>
/// <strong>Switchable off through <c>Datenbank:Wandern</c>.</strong> Not for
/// convenience but for the case where an operator deliberately runs the schema
/// migration by hand: a restart must then not start it after all. The default
/// is on, because a service that starts without its schema answers every
/// request with an error.
/// </para>
/// </remarks>
public static class SchemaMigration
{
    /// <summary>The switch in the configuration.</summary>
    public const string Key = "Datenbank:Wandern";

    /// <summary>How long a database that is still starting up is waited for.</summary>
    /// <remarks>
    /// <strong>Not convenience but necessity.</strong> In compose
    /// <c>pg_isready</c> reports "ready" while Postgres is still running its
    /// setup scripts on a server that listens only locally — the service then
    /// gets "connection refused". In Kubernetes there is no <c>depends_on</c>
    /// at all: there the container starts whenever it likes. A service that
    /// dies of that is wrong in both environments.
    /// <para>
    /// One minute is a choice, not a derivation: long enough for a Postgres
    /// coming up for the first time, short enough that a genuine
    /// misconfiguration does not look like hanging. After that the error flies
    /// — <em>not</em> serving on in silence: a service without its schema
    /// answers every request with an error, and that would be the worse state.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Geduld = TimeSpan.FromMinutes(1);

    /// <summary>Migrates this service's schema, if switched on.</summary>
    /// <typeparam name="TKontext">The context the schema belongs to.</typeparam>
    /// <param name="app">The built application.</param>
    public static async Task MigrateSchemaAsync<TKontext>(this WebApplication app)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue(Key, defaultValue: true))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();

        var protokoll = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SchemaMigration));

        protokoll.LogInformation("Wandere das Schema von {Kontext}", typeof(TKontext).Name);

        var context = scope.ServiceProvider.GetRequiredService<TKontext>();
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        var pause = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                await context.Database.MigrateAsync();
                return;
            }
            catch (Exception error) when (IsTransient(error) && uhr.Elapsed < Geduld)
            {
                // The kind of failure, never the connection string: a password
                // stands in it, and a log is also a place where something ends
                // up.
                protokoll.LogWarning(
                    "Datenbank noch nicht bereit ({Art}), erneut in {Pause}s",
                    error.GetType().Name, pause.TotalSeconds);

                await Task.Delay(pause);

                // Growing but capped: the first attempts should take hold
                // quickly, the later ones must not hammer every second.
                pause = TimeSpan.FromSeconds(Math.Min(pause.TotalSeconds * 2, 8));
            }
        }
    }

    /// <summary>Is this a database still starting up — or an error?</summary>
    /// <remarks>
    /// <strong>The distinction is the whole point.</strong> The first draft
    /// caught every exception, and a genuine schema error thereby became a
    /// minute of waiting with a misleading last message: the schema migration
    /// started over nine times and reported "type already exists" at the end —
    /// created by the first attempt, whose true cause nobody could see any
    /// more.
    /// <para>
    /// <c>DbException.IsTransient</c> is the question the provider answers
    /// itself; a <c>SocketException</c> in the chain is the case where nobody
    /// has answered at all yet.
    /// </para>
    /// </remarks>
    private static bool IsTransient(Exception error)
    {
        for (var glied = error; glied is not null; glied = glied.InnerException)
        {
            if (glied is System.Net.Sockets.SocketException)
            {
                return true;
            }

            if (glied is System.Data.Common.DbException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
