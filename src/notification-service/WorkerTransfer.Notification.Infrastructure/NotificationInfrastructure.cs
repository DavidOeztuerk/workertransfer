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
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Notification.Contracts;

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

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>Was dieser Dienst über sich selbst beantworten kann (ADR-0044).</summary>
    /// <remarks>
    /// <para>Sie stehen hier und nicht in der Dienstgrundlage, aus demselben
    /// Grund, aus dem der Ledger, die Prüfspur und der Speicher hier stehen: es
    /// sind Entscheidungen <em>dieses</em> Dienstes.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> fehlt hier absichtlich — sie liest nur die
    /// Konfiguration, ist damit für jeden Dienst dieselbe und steht in
    /// <c>AddNachweis</c>. Sie ist die eine, die ein neuer Dienst nicht
    /// vergessen kann.</para>
    /// </remarks>
    private static void Nachweis(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPruefung>(_ => new Zahlpruefung(
            [typeof(Benachrichtigungsart).Assembly, typeof(BenachrichtigenV1).Assembly]));

        // Keine KI-Naht: `Anbieterpruefung` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(false, null));

        var loeschung = new Loescheinstellungen();
        configuration.GetSection(Loescheinstellungen.Abschnitt).Bind(loeschung);

        services.AddScoped<IPruefung>(_ => Loeschpruefung.AlsEmpfaenger(
            "notification",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
