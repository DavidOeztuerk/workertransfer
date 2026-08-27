using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Jobs.Application.Behaviors;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Stellen;
using WorkerTransfer.Jobs.Domain.Stellen;
using WorkerTransfer.Jobs.Infrastructure.Entwurf;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.Jobs.Infrastructure.Rueckzug;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Was hier <em>nicht</em> steht, ist so wichtig wie was hier steht: kein
/// Consent-Tor, weil dieser Dienst nichts über Menschen hält, und keine
/// Outbox — es gibt keine Absicht zu verschicken.
/// </remarks>
public static class JobsInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Entwurf und Geheimnis kommen.</param>
    /// <param name="connectionString">Wo die Stellendatenbank liegt.</param>
    public static IServiceCollection AddJobsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Entwurfseinstellungen>(
            configuration.GetSection(Entwurfseinstellungen.Abschnitt));
        services.Configure<Rueckzugseinstellungen>(
            configuration.GetSection(Rueckzugseinstellungen.Abschnitt));

        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.AuchAusDemCookie());

        services.AddSingleton(_ => JobsDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<JobsDbContext>((anbieter, optionen) =>
            JobsDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IStellenspeicher, EfStellenspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Ohne hinterlegten Schlüssel wird kein Fremddienst gerufen. Das ist
        // die Voreinstellung und kein Rückfall.
        var entwurf = new Entwurfseinstellungen();
        configuration.GetSection(Entwurfseinstellungen.Abschnitt).Bind(entwurf);

        if (string.IsNullOrEmpty(entwurf.Schluessel))
        {
            services.AddScoped<IEntwerfer, KeinEntwerfer>();
        }
        else
        {
            services.AddScoped<IEntwerfer, HttpEntwerfer>();
        }

        services.AddCQRS(typeof(StelleAnlegenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
