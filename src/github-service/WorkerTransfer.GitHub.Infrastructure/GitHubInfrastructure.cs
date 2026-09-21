using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.GitHub.Application.Behaviors;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Application.Connections;
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.GitHub.Infrastructure.Consent;
using WorkerTransfer.GitHub.Infrastructure.Erasure;
using WorkerTransfer.GitHub.Infrastructure.Network;
using WorkerTransfer.GitHub.Infrastructure.Persistence;
using WorkerTransfer.GitHub.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.GitHub.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.GitHub.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Was hier <em>nicht</em> steht: kein Hintergrunddienst, keine Schleife, kein
/// Zeitgeber. GitHub wird gefragt, wenn ein Mensch es verlangt — nie von allein
/// (ADR-0004). Und keine Outbox: dieser Dienst verschickt nichts.
/// </remarks>
public static class GitHubInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where ledger, GitHub and secrets come from.</param>
    /// <param name="connectionString">Where the GitHub database lies.</param>
    public static IServiceCollection AddGitHubInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<GitHubSettings>(
            configuration.GetSection(GitHubSettings.Section));
        services.Configure<GitHubSignInSettings>(
            configuration.GetSection(GitHubSignInSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));


        services.AddSingleton(_ => GitHubDbContextFactory.DataSource(connectionString));
        services.AddDbContext<GitHubDbContext>((provider, options) =>
            GitHubDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<GitHubDbContext>("github");
        services.AddKeyRing<GitHubDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IConnectionStore, EfConnectionStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();
        services.AddScoped<IConsentGate, HttpConsentGate>();
        services.AddScoped<ICallerToken, HttpCallerToken>();
        services.AddScoped<IGitHub, HttpGitHub>();
        // Ohne Zugangsdaten meldet sie sich als „nicht eingerichtet", und der
        // The gist stays the path. It is registered all the same — a service
        // whose dependencies differ by configuration falls over at the first
        // call instead of at startup.
        services.AddScoped<IGitHubSignIn, HttpGitHubSignIn>();

        services.AddCQRS(typeof(ConnectCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Nachweis(services, configuration);

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
    private static void Nachweis(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(LoginError).Assembly, typeof(RepositoryV1).Assembly]));

        // No AI seam: a `ProviderCheck` without a source reports NotApplicable,
        // and that is expressly NOT a green tick — a tick on something that
        // does not apply here at all would be noise in the very document that
        // exists to cut through noise.
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
            "github",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            auditTrailPresent: false));
    }
}
