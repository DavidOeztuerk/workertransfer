using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WorkerTransfer.Outbox;

/// <summary>Wires the outbox into a service.</summary>
public static class OutboxRegistrierung
{
    /// <summary>
    /// Adds recording, delivering and the loop.
    /// </summary>
    /// <remarks>
    /// The delivery itself is the service's: only it knows who its recipients
    /// are and what a kind means. Register an <see cref="IDelivery"/> of your
    /// own before or after this.
    /// </remarks>
    /// <typeparam name="TKontext">The context whose database holds the table.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the pacing comes from.</param>
    /// <param name="anpassen">Anything the service decides rather than configures.</param>
    public static IServiceCollection AddOutbox<TKontext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OutboxSettings>? anpassen = null)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var einstellungen = new OutboxSettings();
        configuration.GetSection(OutboxSettings.Section).Bind(einstellungen);
        anpassen?.Invoke(einstellungen);

        services.AddSingleton(einstellungen);
        services.AddScoped<IOutbox, EfOutbox<TKontext>>();
        services.AddScoped<OutboxDispatcher<TKontext>>();
        services.AddHostedService<OutboxSchleife<TKontext>>();

        return services;
    }
}
