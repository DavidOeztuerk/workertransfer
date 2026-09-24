using Noelia.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Profile.Application.Behaviors;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Domain.Audit;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Infrastructure.Consent;
using WorkerTransfer.Profile.Infrastructure.Draft;
using WorkerTransfer.Profile.Infrastructure.Erasure;
using WorkerTransfer.Profile.Infrastructure.Internal;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.ServiceDefaults;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Profile.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// Everything this service decides for itself stands here in one call — and
/// just as loudly what does <em>not</em> stand here. There is no cache and none
/// to register: nothing implements Noelia's <c>ICacheableQuery</c>, so
/// <c>AddCQRS</c> leaves both cache behaviors out of the pipeline entirely. That
/// is no thrift, that is ADR-0013 — a revocation must take effect at the next
/// read.
/// <para>
/// And there is <strong>no draft provider</strong> as long as none is set up.
/// Without a key not a word of this system leaves the system, and the interface
/// says so instead of keeping quiet about it.
/// </para>
/// </remarks>
public static class ProfileInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where ledger, draft and secret come from.</param>
    /// <param name="connectionString">Where the profile database lies.</param>
    public static IServiceCollection AddProfileInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<DraftSettings>(
            configuration.GetSection(DraftSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));
        // The internal door through which scout-service searches. Empty means: shut.
        services.Configure<NotifySettings>(
            configuration.GetSection(NotifySettings.Section));

        services.AddSingleton(_ => ProfileDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ProfileDbContext>((provider, options) =>
            ProfileDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ProfileDbContext>("profile");
        services.AddKeyRing<ProfileDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProfileStore, EfProfileStore>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();

        // The null drafter is the default, not the fallback: without a deposited
        // key no foreign service is called, and the service says so instead of
        // handing out a template that looks like a suggestion.
        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);

        if (string.IsNullOrEmpty(draft.ApiKey))
        {
            services.AddScoped<IDrafter, NoDrafter>();
        }
        else
        {
            services.AddScoped<IDrafter, HttpDrafter>();
        }

        services.AddCQRS(typeof(StoreProfileCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration, draft);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para><strong>They stand here and not in the ServiceFoundation</strong>,
    /// for the same reason the ledger, the audit trail and the drafter stand
    /// here: they are this service's decisions. <em>Which</em> four fields may
    /// go out to the model is ADR-0024 §3 for exactly this consumer; a shared
    /// set would be the first step towards a shared prompt with an
    /// <c>if</c>.</para>
    ///
    /// <para><strong><c>wt.grenze.ziele</c> is missing here on purpose.</strong>
    /// It reads only the configuration, is therefore the same for every service,
    /// and so stands in <c>AddEvidence</c> — it is the one a new service cannot
    /// forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services,
        IConfiguration configuration,
        DraftSettings draft)
    {
        var seam = !string.IsNullOrEmpty(draft.ApiKey);

        // THE FIELD SET, pinned — and pinned at what runs.
        //
        // `DraftBoundaryTests` pins the same set at the TREE and is therefore
        // the sharper guard: it falls before anything ships. What it cannot do
        // is STAND in the evidence. The human who has to answer the Annex III
        // question gets written down here what this system sends out to the
        // model — dated, instead of searched for in a test project. The
        // description stays German: it goes into the German evidence document.
        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(DraftSituation),
            ["Headline", "Text", "Skills", "Wish", "Prompt"],
            "für einen Profiltext"));

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(PersonProfile).Assembly, typeof(ProfileHitV1).Assembly]));

        // This service's AI provider is the OPERATOR's: `Draft__*` from the
        // environment file, one for everybody. Access per person stands in
        // identity-service, and there it is counted instead of named.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            "anthropic", draft.BaseUrl, seam));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // Between the question and the ledger stands no further type. That is
        // the precondition at which ADR-0013 fails in practice — a cache in
        // front of it strikes nobody, because everything keeps working, only
        // with the state from before.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "profile",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: true));
    }
}
