using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Jobs.Domain.Sources;
using WorkerTransfer.Jobs.Infrastructure.Sources;
using WorkerTransfer.Jobs.Application.Behaviors;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Jobs;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Infrastructure.Draft;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.Jobs.Infrastructure.Withdrawal;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Ai;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Jobs.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Jobs.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// What does <em>not</em> stand here matters as much as what does: no consent
/// gate, because this service holds nothing about people, and no outbox — there
/// is no intent to send.
/// </remarks>
public static class JobsInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where draft and secret come from.</param>
    /// <param name="connectionString">Where the jobs database lies.</param>
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
        services.AddDbContext<JobsDbContext>((provider, options) =>
            JobsDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<JobsDbContext>("jobs");
        services.AddKeyRing<JobsDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IJobStore, EfJobStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        Sources(services, configuration);

        // Always the HTTP drafter: a company with its own key drafts even when
        // the platform has none. Without either, no foreign service is called
        // and the drafter says so — the default, not a fallback.
        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);

        services.AddCompanyAiAccess(configuration);
        services.AddScoped<IDrafter, HttpDrafter>();

        services.AddCQRS(typeof(CreateJobCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, draft);

        return services;
    }

    /// <summary>Adverts from a company's ATS or own feed (ADR-0052).</summary>
    private static void Sources(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SourceSettings>(configuration.GetSection(SourceSettings.Section));
        services.Configure<IdentityLookupSettings>(configuration.GetSection(IdentityLookupSettings.Section));

        services.AddScoped<IJobSourceStore, EfJobSourceStore>();
        services.AddScoped<ISourceReader, HttpSourceReader>();
        services.AddScoped<ICompanySite, HttpCompanySite>();
        services.AddScoped<ICompanyDomains, HttpCompanyDomains>();

        // A company's page: only ever a public address, checked at connect.
        services.AddHttpClient(HttpCompanySite.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectCallback = PublicAddress.ConnectAsync,
                AllowAutoRedirect = false
            });

        var known = new KnownDomains();
        services.AddSingleton(known);
        services.AddSingleton<IKnownDomains>(known);
        services.AddHostedService<KnownDomainsLoader>();
        services.AddHostedService<SourceSchedule>();

        // Noelia's egress policy, widened by the proven domains with a source —
        // through its interface. Registered by `AddWorkerTransferDefaults`
        // before this line; `SourceTests` resolves it and fails if it is not
        // the widened one.
        var policy = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IEgressPolicy));

        if (policy?.ImplementationInstance is IEgressPolicy inner)
        {
            var settings = new SourceSettings();
            configuration.GetSection(SourceSettings.Section).Bind(settings);

            services.Remove(policy);
            services.AddSingleton<IEgressPolicy>(new CompanyDomainEgress(inner, known, settings.Personio));
        }
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the ServiceFoundation, for the same
    /// reason the store and the drafter stand here: they are <em>this</em>
    /// service's decisions.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is missing here on purpose — it reads only
    /// the configuration, is therefore the same for every service and stands in
    /// <c>AddEvidence</c>. It is the one a new service cannot forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, DraftSettings draft)
    {
        var seam = !string.IsNullOrEmpty(draft.ApiKey);

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(SkillList).Assembly, typeof(JobV1).Assembly]));

        // THE FIELD SET, pinned — and pinned at what runs. `DraftBoundaryTests`
        // pins the same set at the tree; this one stands in the evidence. The
        // description stays German: it goes into the German evidence document.
        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(AdvertDraft),
            ["Title", "Description", "Skills", "Location", "Wish", "Prompt"],
            "für eine Stellenanzeige"));

        // This service's provider is the OPERATOR's: `Draft__*` from the
        // environment file, one for everybody. Access per person stands in
        // identity-service, and there it is counted instead of named.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            draft.Provider, draft.BaseUrl, seam));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // No recipient: an advert belongs to a company, not to a human being
        // (ADR-0027 §2). The company withdrawal is a contract of its own and
        // expressly does not count towards the completeness of an erasure.
        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.WithoutSubjectRows("jobs"));
    }
}
