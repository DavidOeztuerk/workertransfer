using Noelia.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Behaviors;
using WorkerTransfer.Scout.Application.Kandidaten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Infrastructure.Belege;
using WorkerTransfer.Scout.Infrastructure.Benachrichtigung;
using WorkerTransfer.Scout.Infrastructure.Einwilligung;
using WorkerTransfer.Scout.Infrastructure.Entwurf;
using WorkerTransfer.Scout.Infrastructure.Erasure;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.Scout.Infrastructure.Profile;
using WorkerTransfer.Scout.Infrastructure.Jobs;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Scout.Contracts;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// <para>Alles, was dieser Dienst für sich entscheidet, steht hier in einem
/// Aufruf (ADR-0003) — und genauso laut, was <em>nicht</em> hier steht.</para>
///
/// <para><strong>Kein Cache</strong>, und keiner zu registrieren: nichts
/// implementiert Noelias <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c>
/// beide Cache-Behaviors ganz aus der Pipeline. Das ist keine Sparsamkeit, das
/// ist ADR-0013 — ein Revocation muss beim nächsten Lesen wirken. In einem
/// Dienst, der Menschen findet, ist das die wichtigste Zeile, die hier fehlt.</para>
///
/// <para><strong>Kein Hintergrundlauf</strong> ausser der Ausgangsschleife. Es
/// gibt keinen Nachtlauf, der gespeicherte Searches wiederholt und „neue Hit"
/// meldet: das wäre der Hit-Zwischenspeicher mit einem Wecker davor
/// (ADR-0036 Entscheidung 4).</para>
///
/// <para><strong>Kein Postbote und keine Mailadresse.</strong> Der Entwurf einer
/// Ansprache verlässt diesen Dienst über die Antwort an den Browser und auf
/// keinem anderen Weg (Auflage 4).</para>
/// </remarks>
public static class ScoutInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger, Profile, Belege und Secrets kommen.</param>
    /// <param name="connectionString">Wo die Scout-Datenbank liegt.</param>
    public static IServiceCollection AddScoutInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<ProfileSettings>(
            configuration.GetSection(ProfileSettings.Section));
        services.Configure<EvidenceSettings>(
            configuration.GetSection(EvidenceSettings.Section));
        services.Configure<JobSettings>(
            configuration.GetSection(JobSettings.Section));
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));

        services.AddSingleton(_ => ScoutDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ScoutDbContext>((provider, optionen) =>
            ScoutDbContextFactory.Configure(
                optionen, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ScoutDbContext>("scout");
        services.AddKeyRing<ScoutDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ISearchStore, EfSearchStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<IProfileSearch, HttpProfileSearch>();
        services.AddScoped<IEvidenceSource, HttpEvidenceSource>();
        services.AddScoped<IJobs, HttpJobs>();

        // Der Nullentwerfer ist die Voreinstellung, nicht der Rückfall: ohne
        // hinterlegten Schlüssel wird kein Fremddienst gerufen, und der Dienst
        // sagt das, statt eine ResumeTemplate auszugeben, die wie ein Vorschlag
        // aussieht — und die jemand an einen Menschen schickte.
        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);

        if (string.IsNullOrEmpty(draft.Schluessel))
        {
            services.AddScoped<IDrafter, NoDrafter>();
        }
        else
        {
            services.Configure<DraftSettings>(
                configuration.GetSection(DraftSettings.Section));
            services.AddScoped<IDrafter, HttpDrafter>();
        }

        services.AddScoped<IDelivery, HttpBenachrichtigung>();
        services.AddOutbox<ScoutDbContext>(configuration);

        services.AddCQRS(typeof(KandidatenAbfrage).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration, draft);

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
        IServiceCollection services,
        IConfiguration configuration,
        DraftSettings draft)
    {
        var naht = !string.IsNullOrEmpty(draft.Schluessel);

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(Search).Assembly, typeof(HakenV1).Assembly]));

        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(Ansprachelage),
            ["Ueberschrift", "Genannt", "Gesucht", "Wunsch", "Prompt"],
            "für eine Ansprache"));

        // Der Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*` aus
        // der EnvironmentFile, einer fuer alle. Den Zugang je Person haelt
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            "anthropic", draft.Adresse, naht));

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
            "scout",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            pruefspurVorhanden: false));
    }
}
