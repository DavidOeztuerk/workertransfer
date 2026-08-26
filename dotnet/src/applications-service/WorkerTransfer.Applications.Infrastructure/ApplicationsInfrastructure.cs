using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Applications.Application.Behaviors;
using WorkerTransfer.Applications.Application.Bewerbungen;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Applications.Infrastructure.Benachrichtigung;
using WorkerTransfer.Applications.Infrastructure.Einwilligung;
using WorkerTransfer.Applications.Infrastructure.Loeschung;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Applications.Infrastructure.Security;
using WorkerTransfer.Applications.Infrastructure.Stellen;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Alles, was dieser Dienst für sich entscheidet, steht hier in einem Aufruf —
/// und ebenso laut, was nicht hier steht. Es gibt keinen Zwischenspeicher: die
/// Freigaben schreibt dieser Dienst, er liest sie nirgends, und was er schreibt,
/// muss sofort gelten (ADR-0013). Nichts hier ist ein
/// <c>ICacheableQuery</c>, weshalb <c>AddCQRS</c> beide Zwischenspeicher-Glieder
/// gar nicht erst in die Kette hängt.
/// </remarks>
public static class ApplicationsInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Woher Ledger, Stellen und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Bewerbungsdatenbank liegt.</param>
    public static IServiceCollection AddApplicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Stelleneinstellungen>(
            configuration.GetSection(Stelleneinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        // Ein Browser hält das Token als httpOnly-Cookie und kann sonst nichts
        // damit anfangen.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.AuchAusDemCookie());

        services.AddSingleton(_ => ApplicationsDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<ApplicationsDbContext>((provider, options) =>
            ApplicationsDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IBewerbungsspeicher, EfBewerbungsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungsschreiber, HttpEinwilligungsschreiber>();
        services.AddScoped<IStellenauskunft, HttpStellenauskunft>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();

        // Vorher ging die Benachrichtigung nach dem Commit als HTTP-Aufruf
        // hinaus, dessen Fehler geschluckt wurde — die Zusage stimmte, aber die
        // Nachricht war dann für immer weg. Jetzt committet die Absicht mit der
        // Änderung, und ein Zusteller bringt sie (ADR-0025). Zehn Versuche, nicht
        // unbegrenzt: nie aufzugeben ist der Löschung vorbehalten, wo ein toter
        // Empfänger die Vollständigkeit blockieren muss.
        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<ApplicationsDbContext>(configuration);

        services.AddCQRS(typeof(BewerbungAbschickenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
