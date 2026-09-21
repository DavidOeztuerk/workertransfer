using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Jobs.Application.Behaviors;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Jobs;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Infrastructure.Entwurf;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.Jobs.Infrastructure.Rueckzug;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Jobs.Contracts;
using Noelia.Abstractions.Security.Checks;

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

        services.Configure<DraftSettings>(
            configuration.GetSection(DraftSettings.Section));
        services.Configure<WithdrawalSettings>(
            configuration.GetSection(WithdrawalSettings.Section));


        services.AddSingleton(_ => JobsDbContextFactory.DataSource(connectionString));
        services.AddDbContext<JobsDbContext>((provider, optionen) =>
            JobsDbContextFactory.Configure(
                optionen, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<JobsDbContext>("jobs");
        services.AddKeyRing<JobsDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IJobStore, EfJobStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Ohne hinterlegten Schlüssel wird kein Fremddienst gerufen. Das ist
        // die Voreinstellung und kein Rückfall.
        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);

        if (string.IsNullOrEmpty(draft.Schluessel))
        {
            services.AddScoped<IDrafter, NoDrafter>();
        }
        else
        {
            services.AddScoped<IDrafter, HttpDrafter>();
        }

        services.AddCQRS(typeof(CreateJobCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, draft);

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
        IServiceCollection services, DraftSettings draft)
    {
        var naht = !string.IsNullOrEmpty(draft.Schluessel);

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(SkillList).Assembly, typeof(JobV1).Assembly]));

        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(AdvertDraft),
            ["Titel", "Beschreibung", "Faehigkeiten", "Ort", "Wunsch", "Prompt"],
            "für eine Stellenanzeige"));

        // Der Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*` aus
        // der EnvironmentFile, einer fuer alle. Den Zugang je Person haelt
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            "anthropic", draft.Adresse, naht));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // Kein Empfaenger: eine Anzeige gehoert einem Unternehmen, nicht einem Menschen (ADR-0027 §2). Der Firmenrueckzug ist ein eigener Vertrag und zaehlt ausdruecklich nicht zur Vollstaendigkeit einer Erasure.
        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.OhneZeilen("jobs"));
    }
}
