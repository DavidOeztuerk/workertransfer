using Girder.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Advisor.Application.Behaviors;
using WorkerTransfer.Advisor.Application.Gespraeche;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandate;
using WorkerTransfer.Advisor.Infrastructure.Auskunft;
using WorkerTransfer.Advisor.Infrastructure.Benachrichtigung;
using WorkerTransfer.Advisor.Infrastructure.Einwilligung;
using WorkerTransfer.Advisor.Infrastructure.Loeschung;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.Advisor.Infrastructure.Sicherheit;
using WorkerTransfer.Advisor.Infrastructure.Uebergabe;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// <para>Alles, was dieser Dienst für sich entscheidet, steht hier in einem
/// Aufruf (ADR-0003) — und genauso laut, was <em>nicht</em> hier steht.</para>
///
/// <para><strong>Kein Cache</strong>, und keiner zu registrieren: nichts
/// implementiert Girders <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide
/// Cache-Behaviors ganz aus der Pipeline. Das ist keine Sparsamkeit, das ist
/// ADR-0013 — und in einem Dienst, dessen ganzer Zweck gestufte Sichtbarkeit
/// ist, die wichtigste Zeile, die hier fehlt.</para>
///
/// <para><strong>Kein Hintergrundlauf</strong> ausser der Ausgangsschleife. Es
/// gibt keinen Nachtlauf, der Stufen wieder öffnet, keine Erinnerung an ein
/// stehengebliebenes Gespräch und keinen Dienst, der für die Person antwortet.</para>
///
/// <para><strong>Kein Entwerfer.</strong> ADR-0037 Entscheidung 5 lässt die
/// KI-Naht offen, bis feststeht, dass Menschen hier selbst schreiben. Solange
/// das offen ist, gibt es keinen Port dafür — und damit auch keine Frage, was
/// ein Modell über einen Menschen sagen dürfte.</para>
/// </remarks>
public static class AdvisorInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger, Auskünfte und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Berater-Datenbank liegt.</param>
    public static IServiceCollection AddAdvisorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Auskunftseinstellungen>(
            configuration.GetSection(Auskunftseinstellungen.Abschnitt));
        services.Configure<Uebergabeeinstellungen>(
            configuration.GetSection(Uebergabeeinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        services.AddSingleton(_ => AdvisorDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<AdvisorDbContext>((anbieter, optionen) =>
            AdvisorDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<IMandatspeicher, EfMandatspeicher>();
        services.AddScoped<IGespraechsspeicher, EfGespraechsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IVorgangsuebergabe, HttpVorgangsuebergabe>();

        // Eine Klasse, zwei Ports: dieselbe Tür, zwei Fragen. Getrennt
        // registriert, damit ein Handler nur die sieht, die er braucht.
        services.AddScoped<HttpAuskuenfte>();
        services.AddScoped<IFirmenauskunft>(a => a.GetRequiredService<HttpAuskuenfte>());
        services.AddScoped<IPersonenauskunft>(a => a.GetRequiredService<HttpAuskuenfte>());

        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<AdvisorDbContext>(configuration);

        services.AddCQRS(typeof(GespraechEroeffnenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
