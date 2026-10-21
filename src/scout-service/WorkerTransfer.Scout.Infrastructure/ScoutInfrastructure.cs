using WorkerTransfer.Scout.Domain.Audit;
using Noelia.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Behaviors;
using WorkerTransfer.Scout.Application.Candidates;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Infrastructure.Consent;
using WorkerTransfer.Scout.Infrastructure.Draft;
using WorkerTransfer.Scout.Infrastructure.Erasure;
using WorkerTransfer.Scout.Infrastructure.GitHubEvidence;
using WorkerTransfer.Scout.Infrastructure.Jobs;
using WorkerTransfer.Scout.Infrastructure.Notification;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.Scout.Infrastructure.Profile;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Scout.Contracts;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.Scout.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// <para>Everything this service decides for itself stands here in one call
/// (ADR-0003) — and just as loudly, what does <em>not</em> stand here.</para>
///
/// <para><strong>No cache</strong>, and none to register: nothing implements
/// Noelia's <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both cache
/// behaviors out of the pipeline entirely. That is no thrift, that is ADR-0013 —
/// a revocation has to take effect at the next read. In a service that finds
/// people, that is the most important line missing here.</para>
///
/// <para><strong>No background run</strong> apart from the outbox loop. There
/// is no nightly run that repeats saved searches and reports "new hits": that
/// would be the hit cache with an alarm clock in front of it (ADR-0036
/// decision 4).</para>
///
/// <para><strong>No postman and no mail address.</strong> The draft of an
/// approach leaves this service through the answer to the browser and by no
/// other path (condition 4).</para>
/// </remarks>
public static class ScoutInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where ledger, profiles, evidence and secrets come from.</param>
    /// <param name="connectionString">Where the scout database lies.</param>
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
        services.AddDbContext<ScoutDbContext>((provider, options) =>
            ScoutDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ScoutDbContext>("scout");
        services.AddKeyRing<ScoutDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ISearchStore, EfSearchStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<IProfileSearch, HttpProfileSearch>();
        services.AddScoped<IEvidenceSource, HttpEvidenceSource>();
        services.AddScoped<IJobLookup, HttpJobLookup>();

        // Always the HTTP drafter: a company with its own key drafts even when
        // the platform has none (ADR-0051). Without either it says so.
        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);

        services.Configure<DraftSettings>(configuration.GetSection(DraftSettings.Section));
        services.AddCompanyAiAccess(configuration);
        services.AddScoped<IDrafter, HttpDrafter>();

        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<ScoutDbContext>(configuration);

        services.AddCQRS(typeof(CandidatesQuery).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration, draft);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the ServiceFoundation, for the same
    /// reason the ledger, the audit trail and the store stand here: they are
    /// decisions of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is deliberately missing here — it reads only
    /// the configuration, is therefore the same for every service and stands in
    /// <c>AddEvidence</c>. It is the one a new service cannot forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services,
        IConfiguration configuration,
        DraftSettings draft)
    {
        var seam = !string.IsNullOrEmpty(draft.ApiKey);

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(Search).Assembly, typeof(TickV1).Assembly]));

        // The purpose stays German: it is a phrase in the German evidence
        // document ("Der Kontext für eine Ansprache trägt genau …").
        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(ApproachSituation),
            ["Headline", "Named", "Searched", "Wish", "Prompt"],
            "für eine Ansprache"));

        // This service's provider is the OPERATOR's: `Draft__*` from the
        // environment file, one for everybody. identity-service holds the access
        // per person, and there it is counted instead of named.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            draft.Provider, draft.BaseUrl, seam));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // No further type stands between the question and the ledger — the
        // precondition at which ADR-0013 fails in practice. A cache in front of
        // it is noticed by nobody, because everything keeps working, only with
        // the state from a while ago.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        // The erasure leaves one row behind: THAT it ran (ADR-0012). The flag
        // below says so only because this line stands here.
        services.AddScoped<IAuditTrail, EfAuditTrail>();

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "scout",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: true));
    }
}
