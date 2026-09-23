using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Consent.Application.Behaviors;
using WorkerTransfer.Consent.Application.Consents;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;
using WorkerTransfer.Consent.Infrastructure.Erasure;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.Consent.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Consent.Infrastructure.Evidence;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Contracts.Consent;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Consent.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here, in one call: which
/// database, which repositories, which behaviours — and, just as loudly, what
/// is <em>not</em> here. There is no cache of any kind, and there is no
/// distributed cache to register: nothing in this service implements Noelia's
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

        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));


        services.AddSingleton(_ => ConsentDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ConsentDbContext>((provider, options) =>
            ConsentDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ConsentDbContext>("consent");
        services.AddKeyRing<ConsentDbContext>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IConsentLedger, EfConsentLedger>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IFreeTextClearing, EfFreeTextClearing>();
        services.AddScoped<ConsentWriter>();
        services.AddSingleton<ICorrelation, HttpCorrelation>();

        // Seven handlers, so the mediator earns its keep. Registered before the
        // transaction behaviour so that one ends up innermost: Noelia's logging
        // and validation run before anything is written, which is where a
        // rejected command costs nothing.
        services.AddCQRS(typeof(GrantConsentCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the ServiceFoundation, for the same
    /// reason the ledger, the audit trail and the store stand here: they are
    /// decisions of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is deliberately missing here — it reads
    /// nothing but the configuration, is therefore the same for every service,
    /// and stands in <c>AddEvidence</c>. It is the one a new service cannot
    /// forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(AuditAction).Assembly, typeof(ConsentQuestionV1).Assembly]));

        // No AI seam: `ProviderCheck` without a source reports NotApplicable,
        // and that is expressly NOT a green tick — a tick on something that does
        // not apply here at all would be noise in the document that exists to
        // cut through noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // The ledger itself — the one check in the `Ledger` area, and the
        // reason that area exists. It measures a NON-existence (no tenant
        // column), and those are the ones that disappear again most easily.
        services.AddSingleton<ISecurityCheck, LedgerCheck>();

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "consent",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            auditTrailPresent: false));
    }
}
