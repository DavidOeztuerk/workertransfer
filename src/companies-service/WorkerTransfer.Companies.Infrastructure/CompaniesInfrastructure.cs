using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Companies.Application.Behaviors;
using WorkerTransfer.Companies.Application.Ports;
using WorkerTransfer.Companies.Application.Profile;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Companies.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Der kürzeste Kompositionswurzel-Aufruf im ganzen System, und das ist die
/// Aussage: kein Consent-Tor, keine Outbox, keine Zustellung, kein
/// Löschbestand. Ein Arbeitgeberprofil ist eine Aussage des Unternehmens über
/// sich selbst — es gibt niemanden, der einwilligen könnte, niemanden zu
/// benachrichtigen und nichts über einen natürlichen Menschen zu löschen.
/// </remarks>
public static class CompaniesInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Wo die Einstellungen stehen.</param>
    /// <param name="connectionString">Wo die Unternehmensdatenbank liegt.</param>
    public static IServiceCollection AddCompaniesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);


        services.AddSingleton(_ => CompaniesDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<CompaniesDbContext>((provider, options) =>
            CompaniesDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProfilspeicher, EfProfilspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddCQRS(typeof(ProfilSichernBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
