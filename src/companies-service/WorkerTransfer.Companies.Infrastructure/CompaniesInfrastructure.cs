using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Companies.Application.Behaviors;
using WorkerTransfer.Companies.Application.Ports;
using WorkerTransfer.Companies.Application.Profiles;
using WorkerTransfer.Companies.Domain.EmployerProfiles;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Companies.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Companies.Infrastructure;

/// <summary>Answers the ports of the application layer.</summary>
/// <remarks>
/// The shortest composition root in the whole system, and that is the
/// statement: no consent gate, no outbox, no delivery, no erasure inventory.
/// An employer profile is a statement by the company about itself — there is
/// nobody who could consent, nobody to notify and nothing about a natural
/// person to erase.
/// </remarks>
public static class CompaniesInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the settings stand.</param>
    /// <param name="connectionString">Where the companies database lies.</param>
    public static IServiceCollection AddCompaniesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);


        services.AddSingleton(_ => CompaniesDbContextFactory.DataSource(connectionString));
        services.AddDbContext<CompaniesDbContext>((provider, options) =>
            CompaniesDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<CompaniesDbContext>("companies");
        services.AddKeyRing<CompaniesDbContext>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProfileStore, EfProfileStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddCQRS(typeof(SaveProfileCommand).Assembly);
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
            [typeof(EmployerProfile).Assembly, typeof(EmployerProfileV1).Assembly]));

        // No AI seam: a `ProviderCheck` without a source reports NotApplicable,
        // and that is expressly NOT a green tick — a tick on something that
        // does not apply here at all would be noise in the very document that
        // exists to cut through noise.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // No recipient: an employer profile belongs to a company, not to a
        // human being (ADR-0027 §2).
        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.WithoutSubjectRows("companies"));
    }
}
