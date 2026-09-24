using Noelia.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Assessment.Application.Behaviors;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Application.Cases;
using WorkerTransfer.Assessment.Domain.Cases;
using WorkerTransfer.Assessment.Infrastructure.Notification;
using WorkerTransfer.Assessment.Infrastructure.Consent;
using WorkerTransfer.Assessment.Infrastructure.Erasure;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.Assessment.Infrastructure.Security;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Assessment.Contracts;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Infrastructure;

/// <summary>Answers the application layer's ports.</summary>
/// <remarks>
/// <para>Everything this service decides for itself stands here in one call
/// (ADR-0003) — and just as loudly, what does <em>not</em> stand here.</para>
///
/// <para><strong>No cache</strong>, and none to register: nothing implements
/// Noelia's <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both cache
/// behaviors out of the pipeline entirely. That is ADR-0013 — a revocation must
/// take effect at the next read, and here it decides whether a company still
/// sees a human being's work.</para>
///
/// <para><strong>No drafter, and expressly none for the evaluation.</strong>
/// There is no <c>IDrafter</c> port in this service (ADR-0042). A model judging
/// a human being's work would be ADR-0022's black box with a decimal point, only
/// in prose — and a test pins that there is no seam for it here.</para>
///
/// <para><strong>No client for foreign addresses.</strong> The only
/// <c>HttpClient</c> this service uses goes to the ledger and to
/// notification-service; the address a person submits is stored and shown,
/// never fetched.</para>
///
/// <para><strong>No background run</strong> except the outbox loop. In
/// particular none that flips expired deadlines: where a case stands is
/// computed.</para>
/// </remarks>
public static class AssessmentInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the ledger and the secrets come from.</param>
    /// <param name="connectionString">Where the assessment database lives.</param>
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
        services.AddDbContext<AssessmentDbContext>((provider, options) =>
            AssessmentDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<AssessmentDbContext>("assessment");
        services.AddKeyRing<AssessmentDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICallerToken, HttpCallerToken>();
        services.AddScoped<ICaseStore, EfCaseStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();

        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<AssessmentDbContext>(configuration);

        services.AddCQRS(typeof(SetTaskCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the service foundation, for the same
    /// reason the ledger, the audit trail and the store stand here: they are
    /// decisions of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is missing here on purpose — it reads only the
    /// configuration, is therefore the same for every service and stands in
    /// <c>AddEvidence</c>. It is the one a new service cannot forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(InputError).Assembly, typeof(SetTaskV1).Assembly]));

        // No AI seam: `ProviderCheck` without a source reports NichtAnwendbar,
        // and that is expressly NOT a green tick — a tick on something that does
        // not apply here would be noise in the document meant to cut through
        // noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // No further type stands between the question and the ledger — the
        // precondition ADR-0013 fails on in practice. A cache in front of it
        // strikes nobody, because everything keeps working, only with the state
        // from before.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "assessment",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: false));
    }
}
