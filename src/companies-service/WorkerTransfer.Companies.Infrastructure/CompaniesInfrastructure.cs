using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Companies.Application.Behaviors;
using WorkerTransfer.Companies.Application.Ports;
using WorkerTransfer.Companies.Application.Profile;
using WorkerTransfer.Companies.Domain.EmployerProfiles;
using WorkerTransfer.Companies.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Companies.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Companies.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Der kürzeste Kompositionswurzel-Aufruf im ganzen System, und das ist die
/// Aussage: kein Consent-Tor, keine Outbox, keine Zustellung, kein
/// Löschbestand. Ein EmployerProfile ist eine Aussage des Unternehmens über
/// sich selbst — es gibt niemanden, der einwilligen könnte, niemanden zu
/// benachrichtigen und nichts über einen natürlichen Menschen zu löschen.
/// </remarks>
public static class CompaniesInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Wo die Einstellungen stehen.</param>
    /// <param name="connectionString">Wo die Unternehmensdatenbank liegt.</param>
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
            [typeof(EmployerProfile).Assembly, typeof(EmployerProfileV1).Assembly]));

        // None KI-Naht: `ProviderCheck` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // Kein Empfaenger: ein EmployerProfile gehoert einem Unternehmen, nicht einem Menschen (ADR-0027 §2).
        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.WithoutSubjectRows("companies"));
    }
}
