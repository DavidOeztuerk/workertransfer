using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WorkerTransfer.Outbox;

/// <summary>Wires the outbox into a service.</summary>
public static class OutboxRegistration
{
    /// <summary>
    /// Adds recording, delivering and the loop.
    /// </summary>
    /// <remarks>
    /// The delivery itself is the service's: only it knows who its recipients
    /// are and what a kind means. Register an <see cref="IDelivery"/> of your
    /// own before or after this.
    /// </remarks>
    /// <typeparam name="TContext">The context whose database holds the table.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the pacing comes from.</param>
    /// <param name="customize">Anything the service decides rather than configures.</param>
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OutboxSettings>? customize = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new OutboxSettings();
        configuration.GetSection(OutboxSettings.Section).Bind(settings);
        customize?.Invoke(settings);

        services.AddSingleton(settings);
        services.AddScoped<IOutbox, EfOutbox<TContext>>();
        services.AddScoped<OutboxDispatcher<TContext>>();
        services.AddHostedService<OutboxLoop<TContext>>();

        return services;
    }
}
