using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Anfragen;
using WorkerTransfer.Resume.Application.Behaviors;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Resume.Domain.Pruefspur;
using WorkerTransfer.Resume.Infrastructure.Benachrichtigung;
using WorkerTransfer.Resume.Infrastructure.Einwilligung;
using WorkerTransfer.Resume.Infrastructure.Loeschung;
using WorkerTransfer.Resume.Infrastructure.Persistence;
using WorkerTransfer.Resume.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here, in one call — and,
/// just as loudly, what is not here. There is no cache: the release a company
/// holds is read from the ledger on every request, and a cache would mean a
/// withdrawal takes effect whenever the cache feels like it (ADR-0013). Nothing
/// implements Girder's <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both
/// cache behaviours out of the pipeline entirely.
/// </remarks>
public static class ResumeInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the ledger and the secrets come from.</param>
    /// <param name="connectionString">Where the resume database lives.</param>
    public static IServiceCollection AddResumeInfrastructure(
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


        services.AddSingleton(_ => ResumeDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ResumeDbContext>((provider, options) =>
            ResumeDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ILebenslaufSpeicher, EfLebenslaufSpeicher>();
        services.AddScoped<IAnfragenSpeicher, EfAnfragenSpeicher>();
        services.AddScoped<IPruefspur, EfPruefspur>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<HttpKorrelation>();
        services.AddScoped<IKorrelation>(a => a.GetRequiredService<HttpKorrelation>());
        services.AddScoped<IKorrelationsanhang>(a => a.GetRequiredService<HttpKorrelation>());

        // The old path lost a notification whenever the HTTP call failed after
        // the commit — the failure was swallowed, and nobody learned that their
        // request was answered. Now the intent commits with the domain change
        // and a dispatcher delivers it (ADR-0025). Ten attempts, not unlimited:
        // never giving up is reserved for an erasure, where a dead recipient
        // must block the completion.
        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<ResumeDbContext>(configuration);

        services.AddCQRS(typeof(LebenslaufAnfragenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
