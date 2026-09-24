using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.DocumentStore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Behaviors;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Application.Requests;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Documents;
using WorkerTransfer.Resume.Domain.Requests;
using WorkerTransfer.Resume.Domain.Resumes;
using WorkerTransfer.Resume.Infrastructure.Consent;
using WorkerTransfer.Resume.Infrastructure.Erasure;
using WorkerTransfer.Resume.Infrastructure.Notification;
using WorkerTransfer.Resume.Infrastructure.Persistence;
using WorkerTransfer.Resume.Infrastructure.Security;
using WorkerTransfer.Resume.Infrastructure.TextRecognition;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Resume.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Resume.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here, in one call — and,
/// just as loudly, what is not here. There is no cache: the release a company
/// holds is read from the ledger on every request, and a cache would mean a
/// withdrawal takes effect whenever the cache feels like it (ADR-0013). Nothing
/// implements Noelia's <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both
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

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));

        services.AddSingleton(_ => ResumeDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ResumeDbContext>((provider, options) =>
            ResumeDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ResumeDbContext>("resume");
        services.AddKeyRing<ResumeDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IResumeStore, EfResumeStore>();
        services.AddScoped<IRequestStore, EfRequestStore>();
        services.AddScoped<IDocumentRecordStore, EfDocumentRecordStore>();
        services.AddScoped<ITermStore, EfTermStore>();

        // THE TEXT RECOGNISER — a library in the process, not a service on the
        // network (ADR-0043). The decision stands here, and here it would have
        // to be changed: whoever ever uses a foreign recogniser writes its
        // address into the configuration on the same line — otherwise the
        // sovereignty boundary refuses the call without logging a line, and the
        // button simply does nothing.
        services.AddSingleton<ITextRecogniser, PdfTextRecogniser>();

        // The document store from ADR-0021, back in .NET (ADR-0035). It stands
        // here and not in `ServiceDefaults`: whether a service holds files is a
        // decision, and decisions belong in the composition root of the service
        // that makes them (ADR-0003).
        services.AddDocumentStore(configuration);
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<ICallerToken, HttpCallerToken>();
        services.AddScoped<HttpCorrelation>();
        services.AddScoped<ICorrelation>(provider => provider.GetRequiredService<HttpCorrelation>());
        services.AddScoped<IAuditEntryFactory>(
            provider => provider.GetRequiredService<HttpCorrelation>());

        // The old path lost a notification whenever the HTTP call failed after
        // the commit — the failure was swallowed, and nobody learned that their
        // request was answered. Now the intent commits with the domain change
        // and a dispatcher delivers it (ADR-0025). Ten attempts, not unlimited:
        // never giving up is reserved for an erasure, where a dead recipient
        // must block the completion.
        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<ResumeDbContext>(configuration);

        services.AddCQRS(typeof(RequestResumeCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the ServiceFoundation, for the same
    /// reason the ledger, the audit trail and the store stand here: they are
    /// decisions of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is missing here on purpose — it reads only
    /// the configuration, is therefore the same for every service and stands in
    /// <c>AddEvidence</c>. It is the one a new service cannot forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(RequestState).Assembly, typeof(PositionV1).Assembly]));

        // No AI seam: `ProviderCheck` without a source reports NotApplicable,
        // and that is expressly NOT a green tick — a tick on something that does
        // not apply here would be noise in the document meant to cut through
        // noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // No further type stands between the question and the ledger — the
        // precondition on which ADR-0013 fails in practice. A cache in front of
        // it strikes nobody, because everything keeps working, only with the
        // state from a moment ago.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "resume",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            auditTrailPresent: true));
    }
}
