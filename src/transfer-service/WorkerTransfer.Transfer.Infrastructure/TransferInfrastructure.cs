using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Behaviors;
using WorkerTransfer.Transfer.Application.Market;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Requests;
using WorkerTransfer.Transfer.Domain.Market;
using WorkerTransfer.Transfer.Domain.Cases;
using WorkerTransfer.Transfer.Infrastructure.Notification;
using WorkerTransfer.Transfer.Infrastructure.Consent;
using WorkerTransfer.Transfer.Infrastructure.Erasure;
using WorkerTransfer.Transfer.Infrastructure.Persistence;
using WorkerTransfer.Transfer.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Transfer.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Transfer.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// No cache — and at this service that weighs heaviest: what is released here
/// is the statement "I am listening", and a revocation has to take effect at
/// the very next read (ADR-0013). Nothing here is an <c>ICacheableQuery</c>,
/// which is why <c>AddCQRS</c> does not hang either caching link into the chain
/// in the first place.
/// </remarks>
public static class TransferInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where ledger and secrets come from.</param>
    /// <param name="connectionString">Where the transfer database lies.</param>
    public static IServiceCollection AddTransferInfrastructure(
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


        services.AddSingleton(_ => TransferDbContextFactory.DataSource(connectionString));
        services.AddDbContext<TransferDbContext>((provider, options) =>
            TransferDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<TransferDbContext>("transfer");
        services.AddKeyRing<TransferDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IMarketStore, EfMarketStore>();
        services.AddScoped<IRequestStore, EfRequestStore>();
        services.AddScoped<ITransferStore, EfTransferStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<ICallerToken, HttpCallerToken>();

        // The intent commits with the change, a dispatcher delivers it
        // (ADR-0025). Ten attempts, not unlimited: never giving up is reserved
        // for the erasure, where a dead recipient has to block completeness.
        services.AddScoped<IDelivery, HttpNotifier>();
        services.AddOutbox<TransferDbContext>(configuration);

        services.AddCQRS(typeof(SaveMarketStatusCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>Four names where <c>note</c> is a genuine homonym.</summary>
    /// <remarks>
    /// <strong>Public, because it has two readers</strong>:
    /// <c>wt.ki.keine-zahl</c> at run time and <c>VocabularyTests</c> at build
    /// time.
    /// <para>
    /// Everywhere it is the <em>remark</em> — what a person writes about their
    /// market status, and what a company adds to its offer. The German word
    /// would be the most dangerous vocabulary a placement platform can have,
    /// and therefore stands on the list.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TermException> Wortausnahmen { get; } =
    [
        new("MarketStatusV1.Note", Note),
        new("WriteMarketStatusV1.Note", Note),
        new("AngebotV1.Note", Note),
        new("TransferV1.OfferNote", Note)
    ];

    private const string Note =
        "englisch note = Anmerkung — was die Person zu ihrem Marktstatus "
        + "schreibt, beziehungsweise was ein Unternehmen zu seinem Angebot "
        + "dazusagt; deutsch Note waere eine Bewertung und ist hier nicht gemeint";

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
    private static void Nachweis(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(RequestState).Assembly, typeof(MarketStatusV1).Assembly],
            Wortausnahmen));

        // None KI-Naht: `ProviderCheck` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // Zwischen der Frage und dem Ledger steht kein weiterer Typ — die
        // Vorbedingung, an der ADR-0013 in der Praxis scheitert. Ein
        // Zwischenspeicher davor faellt niemandem auf, weil alles
        // weiterfunktioniert, nur eben mit dem State von vorhin.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "transfer",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            auditTrailPresent: false));
    }
}
