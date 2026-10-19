using WorkerTransfer.Applications.Domain.Audit;
using Noelia.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Applications.Application.Behaviors;
using WorkerTransfer.Applications.Application.Applications;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Companies;
using WorkerTransfer.Applications.Infrastructure.Consent;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;
using WorkerTransfer.Applications.Infrastructure.Erasure;
using WorkerTransfer.Applications.Infrastructure.Jobs;
using WorkerTransfer.Applications.Infrastructure.Lookup;
using WorkerTransfer.Applications.Infrastructure.Notification;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Applications.Infrastructure.Security;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Applications.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Applications.Infrastructure;

/// <summary>Answers the application layer's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here in one call — and
/// just as loudly what does not stand here. There is no cache: this service
/// writes the releases, reads them nowhere, and what it writes must take effect
/// at once (ADR-0013). Nothing here is an <c>ICacheableQuery</c>, which is why
/// <c>AddCQRS</c> does not even hang the two caching links into the chain.
/// </remarks>
public static class ApplicationsInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where ledger, jobs and secrets come from.</param>
    /// <param name="connectionString">Where the applications database lives.</param>
    public static IServiceCollection AddApplicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<JobSettings>(
            configuration.GetSection(JobSettings.Section));
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.Section));
        services.Configure<CompanyMemberSettings>(
            configuration.GetSection(CompanyMemberSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));
        services.Configure<CoverLetterSettings>(
            configuration.GetSection(CoverLetterSettings.Section));
        services.Configure<LookupSettings>(
            configuration.GetSection(LookupSettings.Section));

        services.AddSingleton(_ => ApplicationsDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ApplicationsDbContext>((provider, options) =>
            ApplicationsDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ApplicationsDbContext>("applications");
        services.AddKeyRing<ApplicationsDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IApplicationStore, EfApplicationStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();
        services.AddScoped<IConsentWriter, HttpConsentWriter>();
        services.AddScoped<IJobLookup, HttpJobLookup>();
        services.AddScoped<ICallerToken, HttpCallerToken>();
        services.AddScoped<IDraftStore, EfDraftStore>();

        // One's own details and the company name — both over HTTP, both with the
        // caller's token. One adapter for three ports, because it uses the same
        // connection and the same settings; separate classes would be separate
        // places where a time limit can be missing.
        services.AddScoped<HttpApplicantLookup>();
        services.AddScoped<IApplicantLookup>(
            provider => provider.GetRequiredService<HttpApplicantLookup>());
        services.AddScoped<ICompanyNameLookup>(
            provider => provider.GetRequiredService<HttpApplicantLookup>());
        services.AddScoped<IContactLookup>(
            provider => provider.GetRequiredService<HttpApplicantLookup>());

        services.AddScoped<ICompanyMemberLookup, HttpCompanyMemberLookup>();

        // The access comes from the person's account settings (Ollama, MiniMax,
        // Anthropic). Draft__ApiKey stays a fallback for the platform, not
        // the only source — otherwise writing answers 503 while the interface
        // already shows a provider.
        services.AddScoped<IAiAccessLookup, HttpAiAccessLookup>();
        services.AddScoped<ICoverLetterWriter, HttpCoverLetterWriter>();
        services.AddSingleton<CoverLetterWorker>();
        services.AddSingleton<ICoverLetterQueue>(provider =>
            provider.GetRequiredService<CoverLetterWorker>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<CoverLetterWorker>());

        // Before, the notification went out after the commit as an HTTP call
        // whose failure was swallowed — the promise held, but the message was
        // then gone for ever. Now the intent commits with the change, and a
        // dispatcher delivers it (ADR-0025). Ten attempts, not unlimited: never
        // giving up is reserved for the erasure, where a dead recipient must
        // block completeness.
        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<ApplicationsDbContext>(configuration);

        services.AddCQRS(typeof(SubmitApplicationCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>Two names in which <c>quote</c> is a genuine homonym.</summary>
    /// <remarks>
    /// <strong>Public, because it has two readers</strong>: <c>wt.ki.keine-zahl</c>
    /// at runtime and <c>VocabularyTests</c> at build time. Two lists about the
    /// same question drift apart, and the quieter of the two would grow.
    /// <para>
    /// <c>Quote</c> here is the <em>citation</em>: the passage in the cover
    /// letter a comment refers to — verbatim, so the revision knows what is
    /// meant. The German word would be a ratio and rightly stands on the list.
    /// The reason stays German: it goes into the German evidence document.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TermExemption> TermExemptions { get; } =
    [
        new("AddCommentV1.Quote", QuoteReason),
        new("CommentV1.Quote", QuoteReason)
    ];

    private const string QuoteReason =
        "englisch quote = Zitat, die woertliche Stelle im eigenen Anschreiben, "
        + "auf die sich die Anmerkung bezieht; deutsch Quote waere ein "
        + "Verhaeltnis und gehoert auf die Liste";

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the service foundation, for the same
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
        var coverLetter = new CoverLetterSettings();
        configuration.GetSection(CoverLetterSettings.Section).Bind(coverLetter);

        // The OPERATOR's key is only the fallback here: the person chooses the
        // access in her account settings, and only identity-service knows it.
        // What this service can say about providers is therefore half — the
        // other half stands in `identity-service`, counted.
        var seam = !string.IsNullOrEmpty(coverLetter.ApiKey);

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(ApplicationState).Assembly, typeof(ApplicationV1).Assembly],
            TermExemptions));

        // THE FIELD SET, pinned — and pinned at what runs. `DraftRulesTests`
        // pins the same set at the tree; this one stands in the evidence. The
        // description stays German: it goes into the German evidence document.
        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(CoverLetterContext),
            ["JobTitle", "Company", "JobLocation", "JobDescription", "RequestedSkills", "OwnName", "OwnHeadline", "OwnText", "OwnSkills", "OwnCareer", "Language"],
            "für ein Anschreiben"));

        // This service's provider is the OPERATOR's: `Draft__*` from the
        // environment file, one for everybody. Access per person stands in
        // identity-service, and there it is counted instead of named.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            "anthropic", coverLetter.BaseUrl, seam));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        // The erasure leaves one row behind: THAT it ran (ADR-0012). The flag
        // below says so only because this line stands here.
        services.AddScoped<IAuditTrail, EfAuditTrail>();

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "applications",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: true));
    }
}
