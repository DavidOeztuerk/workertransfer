using Girder.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Assessment.Application.Behaviors;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Application.Vorgaenge;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.Assessment.Infrastructure.Benachrichtigung;
using WorkerTransfer.Assessment.Infrastructure.Einwilligung;
using WorkerTransfer.Assessment.Infrastructure.Loeschung;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.Assessment.Infrastructure.Sicherheit;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// <para>Alles, was dieser Dienst für sich entscheidet, steht hier in einem
/// Aufruf (ADR-0003) — und genauso laut, was <em>nicht</em> hier steht.</para>
///
/// <para><strong>Kein Cache</strong>, und keiner zu registrieren: nichts
/// implementiert Girders <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide
/// Cache-Behaviors ganz aus der Pipeline. Das ist ADR-0013 — ein Widerruf muss
/// beim nächsten Lesen wirken, und hier entscheidet er darüber, ob ein
/// Unternehmen die Arbeit eines Menschen noch sieht.</para>
///
/// <para><strong>Kein Entwerfer, und ausdrücklich keiner für die
/// Bewertung.</strong> Es gibt in diesem Dienst keinen <c>IEntwerfer</c>-Port
/// (ADR-0042). Ein Modell, das die Arbeit eines Menschen beurteilt, wäre die
/// Black Box mit Komma aus ADR-0022, nur in Prosa — und ein Test hält fest,
/// dass es hier keine Naht dafür gibt.</para>
///
/// <para><strong>Kein Klient für fremde Adressen.</strong> Der einzige
/// <c>HttpClient</c>, den dieser Dienst benutzt, geht an den Ledger und an
/// notification-service; die Adresse, die eine Person einreicht, wird
/// gespeichert und angezeigt, nie abgerufen.</para>
///
/// <para><strong>Kein Hintergrundlauf</strong> ausser der Ausgangsschleife.
/// Insbesondere keiner, der abgelaufene Fristen umschaltet: wo ein Vorgang
/// steht, wird gerechnet.</para>
/// </remarks>
public static class AssessmentInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Arbeitsproben-Datenbank liegt.</param>
    public static IServiceCollection AddAssessmentInfrastructure(
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

        services.AddSingleton(_ => AssessmentDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<AssessmentDbContext>((anbieter, optionen) =>
            AssessmentDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<IVorgangsspeicher, EfVorgangsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();

        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<AssessmentDbContext>(configuration);

        services.AddCQRS(typeof(AufgabeStellenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
