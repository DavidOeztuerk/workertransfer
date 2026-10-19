using WorkerTransfer.Advisor.Domain.Audit;
using Noelia.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Advisor.Application.Behaviors;
using WorkerTransfer.Advisor.Application.Conversations;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Domain.Mandates;
using WorkerTransfer.Advisor.Infrastructure.Consent;
using WorkerTransfer.Advisor.Infrastructure.Erasure;
using WorkerTransfer.Advisor.Infrastructure.Handover;
using WorkerTransfer.Advisor.Infrastructure.Lookup;
using WorkerTransfer.Advisor.Infrastructure.Notification;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.Advisor.Infrastructure.Security;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Advisor.Contracts;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// <para>Everything this service decides for itself stands here in one call
/// (ADR-0003) — and just as loudly, what does <em>not</em> stand here.</para>
///
/// <para><strong>No cache</strong>, and none to register: nothing implements
/// Noelia's <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both cache
/// behaviors out of the pipeline altogether. That is not thrift, that is
/// ADR-0013 — and in a service whose whole purpose is staged visibility, the
/// most important line missing here.</para>
///
/// <para><strong>No background run</strong> apart from the outbox loop. There is
/// no nightly run that reopens stages, no reminder about a stalled conversation
/// and no service that answers on the person's behalf.</para>
///
/// <para><strong>No drafter.</strong> ADR-0037 decision 5 leaves the AI seam
/// open until it is settled that people write here themselves. As long as that
/// is open there is no port for it — and therefore no question what a model
/// might say about a human being.</para>
/// </remarks>
public static class AdvisorInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the ledger, the lookups and the secrets come from.</param>
    /// <param name="connectionString">Where the advisor database lives.</param>
    public static IServiceCollection AddAdvisorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<LookupSettings>(
            configuration.GetSection(LookupSettings.Section));
        services.Configure<HandoverSettings>(
            configuration.GetSection(HandoverSettings.Section));
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));

        services.AddSingleton(_ => AdvisorDbContextFactory.DataSource(connectionString));
        services.AddDbContext<AdvisorDbContext>((provider, options) =>
            AdvisorDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<AdvisorDbContext>("advisor");
        services.AddKeyRing<AdvisorDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICallerToken, HttpCallerToken>();
        services.AddScoped<IMandateStore, EfMandateStore>();
        services.AddScoped<IConversationStore, EfConversationStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<ICaseHandover, HttpCaseHandover>();

        // One class, two ports: the same door, two questions. Registered
        // separately, so that a handler sees only the one it needs.
        services.AddScoped<HttpLookups>();
        services.AddScoped<ICompanyLookup>(a => a.GetRequiredService<HttpLookups>());
        services.AddScoped<IPersonLookup>(a => a.GetRequiredService<HttpLookups>());

        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<AdvisorDbContext>(configuration);

        services.AddCQRS(typeof(OpenConversationCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>
    /// Seven names in which a word of the forbidden list is a genuine homonym.
    /// </summary>
    /// <remarks>
    /// <para><strong>Public, because it has two readers.</strong>
    /// <c>wt.ki.keine-zahl</c> reads it at run time, <c>VocabularyTests</c> at
    /// build time. Two lists about the same question drift apart, and the
    /// quieter of the two would grow — here neither grows: every line stands in
    /// the finding a works council reads.</para>
    ///
    /// <para><c>WorkloadPercent</c> is the workload the person states themselves
    /// for their next job — a statement about a <em>job</em>. <c>Note</c> is
    /// English and means remark: a company's first sentence to a human being.
    /// The German word would be a mark, and that is exactly why it is on the
    /// list — it is not meant here.</para>
    ///
    /// <para>The three language catalogues of the UI keep the same shape for
    /// genuine cognates ("Status", "Website", "Administrator"): short, and every
    /// line an individual case.</para>
    ///
    /// <para>The reasons stay German: they appear word for word in the German
    /// evidence document.</para>
    /// </remarks>
    public static IReadOnlyList<TermExemption> TermExemptions { get; } =
    [
        new("MandateV1.WorkloadPercent", WorkloadReason),
        new("WriteMandateV1.WorkloadPercent", WorkloadReason),
        new("ConversationV1.WorkloadPercent", WorkloadReason),
        new("Mandate.WorkloadPercent", WorkloadReason),
        new("OpenConversationV1.Note", NoteReason),
        new("ConversationV1.Note", NoteReason),
        new("MyConversationV1.Note", NoteReason)
    ];

    private const string WorkloadReason =
        "das Pensum, das die Person SELBST fuer ihre naechste Stelle nennt — "
        + "eine Angabe ueber eine Stelle, keine ueber einen Menschen";

    private const string NoteReason =
        "englisch note = Anmerkung, der erste Satz eines Unternehmens an einen "
        + "Menschen; deutsch Note waere eine Bewertung, und genau deshalb steht "
        + "das Wort auf der Liste — hier ist es nicht gemeint";

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
            [typeof(TransitionNotAllowed).Assembly, typeof(MandateV1).Assembly],
            TermExemptions));

        // No AI seam: `ProviderCheck` without a source reports NichtAnwendbar,
        // and that is expressly NOT a green tick — a tick on something that does
        // not apply here at all would be noise in the document meant to cut
        // through noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // No further type stands between the question and the ledger — the
        // precondition ADR-0013 fails on in practice. A cache in front of it is
        // noticed by nobody, because everything keeps working, just with the
        // state from a moment ago.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        // The erasure leaves one row behind: THAT it ran (ADR-0012). The flag
        // below says so only because this line stands here.
        services.AddScoped<IAuditTrail, EfAuditTrail>();

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "advisor",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: true));
    }
}
