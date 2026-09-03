using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Portfolio.Application.Behaviors;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Application.Portfolios;
using WorkerTransfer.Portfolio.Domain.Ablage;
using WorkerTransfer.Portfolio.Domain.Portfolios;
using WorkerTransfer.Portfolio.Infrastructure.Ablage;
using WorkerTransfer.Portfolio.Infrastructure.Einwilligung;
using WorkerTransfer.Portfolio.Infrastructure.Loeschung;
using WorkerTransfer.Portfolio.Infrastructure.Persistence;
using WorkerTransfer.Portfolio.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Alles, was dieser Dienst für sich entscheidet, steht hier — und genauso
/// laut, was nicht hier steht: kein Cache (ADR-0013) und <b>eine</b> Ablage mit
/// <b>einem</b> Backend (ADR-0021).
/// </remarks>
public static class PortfolioInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger, Ablage und Geheimnis kommen.</param>
    /// <param name="connectionString">Wo die Portfoliodatenbank liegt.</param>
    public static IServiceCollection AddPortfolioInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Ablageeinstellungen>(
            configuration.GetSection(Ablageeinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));


        services.AddSingleton(_ => PortfolioDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<PortfolioDbContext>((anbieter, optionen) =>
            PortfolioDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPortfoliospeicher, EfPortfoliospeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddSingleton<IAblage, Dateiablage>();
        services.AddSingleton<IKorrelation, HttpKorrelation>();

        services.AddCQRS(typeof(PortfolioSichernBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
