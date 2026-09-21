using Noelia.Application.Extensions;
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
using WorkerTransfer.Assessment.Infrastructure.Erasure;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.Assessment.Infrastructure.Sicherheit;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Assessment.Contracts;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// <para>Alles, was dieser Dienst für sich entscheidet, steht hier in einem
/// Aufruf (ADR-0003) — und genauso laut, was <em>nicht</em> hier steht.</para>
///
/// <para><strong>Kein Cache</strong>, und keiner zu registrieren: nichts
/// implementiert Noelias <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide
/// Cache-Behaviors ganz aus der Pipeline. Das ist ADR-0013 — ein Revocation muss
/// beim nächsten Lesen wirken, und hier entscheidet er darüber, ob ein
/// Unternehmen die Arbeit eines Menschen noch sieht.</para>
///
/// <para><strong>Kein Entwerfer, und ausdrücklich keiner für die
/// Evaluation.</strong> Es gibt in diesem Dienst keinen <c>IDrafter</c>-Port
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
/// Insbesondere keiner, der abgelaufene Fristen umschaltet: wo ein Case
/// steht, wird gerechnet.</para>
/// </remarks>
public static class AssessmentInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger und Secrets kommen.</param>
    /// <param name="connectionString">Wo die WorkSamples-Datenbank liegt.</param>
    public static IServiceCollection AddAssessmentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));

        services.AddSingleton(_ => AssessmentDbContextFactory.DataSource(connectionString));
        services.AddDbContext<AssessmentDbContext>((provider, optionen) =>
            AssessmentDbContextFactory.Configure(
                optionen, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<AssessmentDbContext>("assessment");
        services.AddKeyRing<AssessmentDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<ICaseStore, EfCaseStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();

        services.AddScoped<IDelivery, HttpBenachrichtigung>();
        services.AddOutbox<AssessmentDbContext>(configuration);

        services.AddCQRS(typeof(SetTaskCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>Was dieser Dienst über sich selbst beantworten kann (ADR-0044).</summary>
    /// <remarks>
    /// <para>Sie stehen hier und nicht in der ServiceFoundation, aus demselben
    /// Grund, aus dem der Ledger, die Prüfspur und der Speicher hier stehen: es
    /// sind Entscheidungen <em>dieses</em> Dienstes.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> fehlt hier absichtlich — sie liest nur die
    /// Konfiguration, ist damit für jeden Dienst dieselbe und steht in
    /// <c>AddEvidence</c>. Sie ist die eine, die ein neuer Dienst nicht
    /// vergessen kann.</para>
    /// </remarks>
    private static void Nachweis(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(Eingabefehler).Assembly, typeof(SetTaskV1).Assembly]));

        // None KI-Naht: `ProviderCheck` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // Zwischen der Frage und dem Ledger steht kein weiterer Typ — die
        // Vorbedingung, an der ADR-0013 in der Praxis scheitert. Ein
        // Zwischenspeicher davor faellt niemandem auf, weil alles
        // weiterfunktioniert, nur eben mit dem State von vorhin.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AlsEmpfaenger(
            "assessment",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            pruefspurVorhanden: false));
    }
}
