using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Notification.Application.Behaviors;
using WorkerTransfer.Notification.Application.Benachrichtigungen;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;
using WorkerTransfer.Notification.Infrastructure.Erasure;
using WorkerTransfer.Notification.Infrastructure.Persistence;
using WorkerTransfer.Notification.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Notification.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Notification.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Was hier <em>nicht</em> steht, ist die Aussage: <strong>kein
/// Postversand</strong> und keine Adresse. Dieser Dienst entscheidet, <em>ob</em>
/// etwas hinausgeht; <em>an wen</em>, weiß allein identity-service. Und keine
/// Outbox: er ist der Empfänger von Absichten, nicht ihr Absender.
/// </remarks>
public static class NotificationInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Woher Adressen und Secrets kommen.</param>
    /// <param name="connectionString">Wo die Benachrichtigungsdatenbank liegt.</param>
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
        services.AddScoped<IPostbote, HttpPostbote>();

        services.AddCQRS(typeof(BenachrichtigenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

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
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(Benachrichtigungsart).Assembly, typeof(BenachrichtigenV1).Assembly]));

        // None KI-Naht: `ProviderCheck` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AlsEmpfaenger(
            "notification",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            pruefspurVorhanden: false));
    }
}
