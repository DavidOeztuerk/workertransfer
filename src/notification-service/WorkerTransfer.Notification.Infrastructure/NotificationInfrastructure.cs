using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Notification.Application.Behaviors;
using WorkerTransfer.Notification.Application.Notifications;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Notifications;
using WorkerTransfer.Notification.Infrastructure.Erasure;
using WorkerTransfer.Notification.Infrastructure.Persistence;
using WorkerTransfer.Notification.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Notification.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Notification.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// What does <em>not</em> stand here is the statement: <strong>no mail
/// dispatch</strong> and no address. This service decides <em>whether</em>
/// something goes out; <em>to whom</em>, identity-service alone knows. And no
/// outbox: it is the recipient of intents, not their sender.
/// </remarks>
public static class NotificationInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where addresses and secrets come from.</param>
    /// <param name="connectionString">Where the notification database lies.</param>
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MailSettings>(
            configuration.GetSection(MailSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));


        services.AddSingleton(_ => NotificationDbContextFactory.DataSource(connectionString));
        services.AddDbContext<NotificationDbContext>((provider, options) =>
            NotificationDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<NotificationDbContext>("notification");
        services.AddKeyRing<NotificationDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPreferenceStore, EfPreferenceStore>();
        services.AddScoped<IInboxStore, EfInboxStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();
        services.AddScoped<IMailer, HttpMailer>();

        services.AddCQRS(typeof(NotifyCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in ServiceFoundation, for the same reason
    /// the ledger, the audit trail and the store stand here: they are decisions
    /// of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is deliberately missing here — it reads only
    /// the configuration, is therefore the same for every service and stands in
    /// <c>AddEvidence</c>. It is the one a new service cannot forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(NotificationKind).Assembly, typeof(NotifyV1).Assembly]));

        // No AI seam: a `ProviderCheck` without a source reports NotApplicable,
        // and that is expressly NOT a green tick — a tick on something that
        // does not apply here at all would be noise in the very document that
        // exists to cut through noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "notification",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: false));
    }
}
