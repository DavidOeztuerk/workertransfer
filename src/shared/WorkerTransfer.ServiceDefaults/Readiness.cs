using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>What a service has to answer before it gets traffic.</summary>
public static class Readiness
{
    /// <summary>How long a silent database may hold the probe up.</summary>
    /// <remarks>
    /// A readiness probe without a time limit hangs for as long as the
    /// connection attempt does, and the orchestrator reads that absence as
    /// healthy until its own limit bites.
    /// </remarks>
    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(5);

    /// <summary>Reports the service ready only once its database answers.</summary>
    /// <remarks>
    /// <c>/health/ready</c> filters by the tag <c>ready</c>. Without a single
    /// entry tagged that way the filtered set is empty, an empty report counts
    /// as healthy, and the address answers 200 — even over an unreachable
    /// database. The orchestrator then sends traffic to a service that answers
    /// every request with a 500.
    /// </remarks>
    /// <typeparam name="TKontext">This service's context.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="serviceName">Names the entry in the health report.</param>
    /// <returns>The container.</returns>
    public static IServiceCollection AddDatabaseReadiness<TKontext>(
        this IServiceCollection services, string serviceName)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            $"{serviceName}-datenbank",
            provider => new DatabaseReadiness(
                provider.GetRequiredService<TKontext>()),
            HealthStatus.Unhealthy,
            ["ready", "db"],
            Geduld));

        return services;
    }
}

/// <summary>Does this service's database answer?</summary>
/// <remarks>
/// <c>CanConnectAsync</c> and no query: a readiness probe runs every second,
/// and a probe that produces load itself takes an already ailing service down
/// completely.
/// </remarks>
/// <param name="dbContext">The context whose connection is checked.</param>
internal sealed class DatabaseReadiness(DbContext dbContext) : IHealthCheck
{
    /// <inheritdoc />
    /// <remarks>
    /// The description names no value. Noelia prints it on
    /// <c>/health/ready</c>, and that address answers everybody — an Npgsql
    /// exception carries host, port and at times the user. The exception
    /// travels along as a second field and ends up in the log, not in the
    /// answer.
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Die Datenbank antwortet.")
                : HealthCheckResult.Unhealthy("Die Datenbank antwortet nicht.");
        }
        catch (Exception error)
        {
            return HealthCheckResult.Unhealthy("Die Datenbank antwortet nicht.", error);
        }
    }
}
