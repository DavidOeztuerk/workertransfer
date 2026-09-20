using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Consent.Application.Behaviors;
using WorkerTransfer.Consent.Application.Einwilligung;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;
using WorkerTransfer.Consent.Infrastructure.Loeschung;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.Consent.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Consent.Infrastructure.Nachweis;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Consent.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here, in one call: which
/// database, which repositories, which behaviours — and, just as loudly, what
/// is <em>not</em> here. There is no cache of any kind, and there is no
/// distributed cache to register: nothing in this service implements Girder's
/// <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both cache behaviours out
/// of the pipeline entirely. That is not thrift, it is ADR-0013 — a withdrawal
/// has to take effect on the very next read, and a cache here is not a
/// performance detail but a broken promise.
/// </remarks>
public static class ConsentInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the erasure secret comes from.</param>
    /// <param name="connectionString">Where the consent database lives.</param>
    public static IServiceCollection AddConsentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));


        services.AddSingleton(_ => ConsentDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ConsentDbContext>((provider, options) =>
            ConsentDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IConsentLedger, EfConsentLedger>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IFreitextraeumung, EfFreitextraeumung>();
        services.AddScoped<Einwilligungsschreiber>();
        services.AddSingleton<IKorrelation, HttpKorrelation>();

        // Seven handlers, so the mediator earns its keep. Registered before the
        // transaction behaviour so that one ends up innermost: Girder's logging
        // and validation run before anything is written, which is where a
        // rejected command costs nothing.
        services.AddCQRS(typeof(EinwilligungErteilenBefehl).Assembly);
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
            [typeof(AuditAction).Assembly, typeof(EinwilligungsfrageV1).Assembly]));

        // Keine KI-Naht: `Anbieterpruefung` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(false, null));

        // Der Ledger selbst — die eine Pruefung im Bereich `Ledger`, und der
        // Grund, dass es diesen Bereich gibt. Sie misst ein NICHT-Vorhandensein
        // (keine Mandantenspalte), und das sind die, die am leichtesten wieder
        // verschwinden.
        services.AddScoped<IPruefung, Ledgerpruefung>();

        var loeschung = new Loescheinstellungen();
        configuration.GetSection(Loescheinstellungen.Abschnitt).Bind(loeschung);

        services.AddScoped<IPruefung>(_ => Loeschpruefung.AlsEmpfaenger(
            "consent",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
