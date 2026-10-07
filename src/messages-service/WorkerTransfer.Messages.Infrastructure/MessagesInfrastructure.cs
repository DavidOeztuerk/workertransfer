using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Noelia.Abstractions.Security.Checks;
using Noelia.Application.Extensions;
using Npgsql;
using WorkerTransfer.Messages.Application.Behaviors;
using WorkerTransfer.Messages.Application.Conversations;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Contracts;
using WorkerTransfer.Messages.Domain.Audit;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Messages.Infrastructure.Companies;
using WorkerTransfer.Messages.Infrastructure.Consent;
using WorkerTransfer.Messages.Infrastructure.Draft;
using WorkerTransfer.Messages.Infrastructure.Erasure;
using WorkerTransfer.Messages.Infrastructure.Notification;
using WorkerTransfer.Messages.Infrastructure.Persistence;
using WorkerTransfer.Messages.Infrastructure.Process;
using WorkerTransfer.Messages.Infrastructure.Security;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Messages.Infrastructure;

/// <summary>Answers the application layer's ports.</summary>
/// <remarks>
/// <para>Everything this service decides for itself stands here in one call
/// (ADR-0003).</para>
///
/// <para><strong>No cache</strong>, and expressly none in front of the process
/// lookup: whether somebody belongs to an application is asked every time
/// (ADR-0047). A member removed from the company must stop at the next
/// request, not at the next expiry.</para>
///
/// <para><strong>No background run</strong> except the outbox loop. Above all
/// none that drafts on its own: a draft comes into being on a click, never
/// because a message arrived (VISION-FRAGEN D2).</para>
/// </remarks>
public static class MessagesInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the addresses and secrets come from.</param>
    /// <param name="connectionString">Where the messages database lives.</param>
    public static IServiceCollection AddMessagesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ApplicationsSettings>(configuration.GetSection(ApplicationsSettings.Section));
        services.Configure<IdentitySettings>(configuration.GetSection(IdentitySettings.Section));
        services.Configure<NotificationSettings>(configuration.GetSection(NotificationSettings.Section));
        services.Configure<ErasureSettings>(configuration.GetSection(ErasureSettings.Section));
        services.Configure<DraftSettings>(configuration.GetSection(DraftSettings.Section));
        services.Configure<ConsentSettings>(configuration.GetSection(ConsentSettings.Section));

        services.AddSingleton(_ => MessagesDbContextFactory.DataSource(connectionString));
        services.AddDbContext<MessagesDbContext>((provider, options) =>
            MessagesDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<MessagesDbContext>("messages");
        services.AddKeyRing<MessagesDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICallerToken, HttpCallerToken>();
        services.AddScoped<IConversationStore, EfConversationStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IProcessLookup, HttpProcessLookup>();
        services.AddScoped<ICompanyMemberLookup, HttpCompanyMemberLookup>();
        services.AddScoped<IReplyDrafter, HttpReplyDrafter>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<IReleaseWriter, HttpReleaseWriter>();
        services.AddScoped<ConversationAccess>();

        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<MessagesDbContext>(configuration);

        services.AddCQRS(typeof(WriteCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    private static void Evidence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(InputError).Assembly, typeof(MessageV1).Assembly]));

        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);
        var seam = !string.IsNullOrEmpty(draft.ApiKey);

        // The platform's provider. A person's own access stands in
        // identity-service, and there it is counted instead of named.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            draft.Provider, draft.BaseUrl, seam));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        // No further type stands between the question and the ledger
        // (ADR-0013): a cache in front of it strikes nobody, because everything
        // keeps working, only with the state from before.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(provider, typeof(HttpConsentGate)));

        services.AddScoped<IAuditTrail, EfAuditTrail>();

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "messages",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: true));
    }
}
