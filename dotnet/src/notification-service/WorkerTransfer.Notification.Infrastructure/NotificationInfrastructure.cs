using Girder.Application.Extensions;
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
using WorkerTransfer.Notification.Infrastructure.Loeschung;
using WorkerTransfer.Notification.Infrastructure.Persistence;
using WorkerTransfer.Notification.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

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
    /// <param name="configuration">Woher Adressen und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Benachrichtigungsdatenbank liegt.</param>
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Posteinstellungen>(
            configuration.GetSection(Posteinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        // Ein Browser hält das Token als httpOnly-Cookie und kann sonst nichts
        // damit anfangen.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.AuchAusDemCookie());

        services.AddSingleton(_ => NotificationDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<NotificationDbContext>((provider, options) =>
            NotificationDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IWunschspeicher, EfWunschspeicher>();
        services.AddScoped<IEingangsspeicher, EfEingangsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IPostbote, HttpPostbote>();

        services.AddCQRS(typeof(BenachrichtigenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
