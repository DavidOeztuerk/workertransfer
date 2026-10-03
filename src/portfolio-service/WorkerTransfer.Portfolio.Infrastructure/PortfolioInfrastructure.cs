using WorkerTransfer.Portfolio.Domain.Audit;
using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Portfolio.Application.Behaviors;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Application.Portfolios;
using WorkerTransfer.Portfolio.Domain.Documents;
using WorkerTransfer.Portfolio.Domain.Portfolios;
using WorkerTransfer.Portfolio.Infrastructure.Documents;
using WorkerTransfer.Portfolio.Infrastructure.Consent;
using WorkerTransfer.Portfolio.Infrastructure.Erasure;
using WorkerTransfer.Portfolio.Infrastructure.Persistence;
using WorkerTransfer.Portfolio.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Portfolio.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Portfolio.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// Everything this service decides for itself stands here — and just as loudly,
/// what does not stand here: no cache (ADR-0013) and <b>one</b> document store
/// with <b>one</b> backend (ADR-0021).
/// </remarks>
public static class PortfolioInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where ledger, document store and secret come from.</param>
    /// <param name="connectionString">Where the portfolio database lies.</param>
    public static IServiceCollection AddPortfolioInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<DocumentStoreSettings>(
            configuration.GetSection(DocumentStoreSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));


        services.AddSingleton(_ => PortfolioDbContextFactory.DataSource(connectionString));
        services.AddDbContext<PortfolioDbContext>((provider, options) =>
            PortfolioDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<PortfolioDbContext>("portfolio");
        services.AddKeyRing<PortfolioDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPortfolioStore, EfPortfolioStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddSingleton<IDocumentStore, FileDocumentStore>();
        services.AddSingleton<ICorrelation, HttpCorrelation>();

        services.AddCQRS(typeof(SavePortfolioCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in ServiceFoundation, for the same reason
    /// the ledger, the audit trail and the store stand here: they are
    /// decisions of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is deliberately missing here — it reads
    /// only the configuration, is therefore the same for every service and
    /// stands in <c>AddEvidence</c>. It is the one a new service cannot
    /// forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(StoredFile).Assembly, typeof(EntryV1).Assembly]));

        // No AI seam: a `ProviderCheck` without a source reports NotApplicable,
        // and that is expressly NOT a green tick — a tick on something that
        // does not apply here at all would be noise in the very document that
        // exists to cut through noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // Between the question and the ledger stands no further type — the
        // precondition ADR-0013 fails on in practice. A cache in front of it
        // occurs to nobody, because everything goes on working, only with the
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
            "portfolio",
            !string.IsNullOrEmpty(erasure.Secret),
            auditTrailPresent: true));
    }
}
