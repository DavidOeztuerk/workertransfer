using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Behaviors;
using WorkerTransfer.Transfer.Application.Markt;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;
using WorkerTransfer.Transfer.Infrastructure.Benachrichtigung;
using WorkerTransfer.Transfer.Infrastructure.Einwilligung;
using WorkerTransfer.Transfer.Infrastructure.Loeschung;
using WorkerTransfer.Transfer.Infrastructure.Persistence;
using WorkerTransfer.Transfer.Infrastructure.Security;

namespace WorkerTransfer.Transfer.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Kein Zwischenspeicher — und bei diesem Dienst wiegt das am schwersten: was
/// hier freigegeben wird, ist die Aussage „ich höre zu", und ein Widerruf muss
/// beim allernächsten Lesezugriff wirken (ADR-0013). Nichts hier ist ein
/// <c>ICacheableQuery</c>, weshalb <c>AddCQRS</c> beide
/// Zwischenspeicher-Glieder gar nicht erst in die Kette hängt.
/// </remarks>
public static class TransferInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Woher Ledger und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Transferdatenbank liegt.</param>
    public static IServiceCollection AddTransferInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));


        services.AddSingleton(_ => TransferDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<TransferDbContext>((provider, options) =>
            TransferDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IMarktspeicher, EfMarktspeicher>();
        services.AddScoped<IAnfragenspeicher, EfAnfragenspeicher>();
        services.AddScoped<ITransferspeicher, EfTransferspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();

        // Die Absicht committet mit der Änderung, ein Zusteller bringt sie
        // (ADR-0025). Zehn Versuche, nicht unbegrenzt: nie aufzugeben ist der
        // Löschung vorbehalten, wo ein toter Empfänger die Vollständigkeit
        // blockieren muss.
        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<TransferDbContext>(configuration);

        services.AddCQRS(typeof(MarktstatusSichernBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
