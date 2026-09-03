using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.GitHub.Application.Behaviors;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Application.Verbindungen;
using WorkerTransfer.GitHub.Domain.Verbindungen;
using WorkerTransfer.GitHub.Infrastructure.Einwilligung;
using WorkerTransfer.GitHub.Infrastructure.Loeschung;
using WorkerTransfer.GitHub.Infrastructure.Netz;
using WorkerTransfer.GitHub.Infrastructure.Persistence;
using WorkerTransfer.GitHub.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Was hier <em>nicht</em> steht: kein Hintergrunddienst, keine Schleife, kein
/// Zeitgeber. GitHub wird gefragt, wenn ein Mensch es verlangt — nie von allein
/// (ADR-0004). Und keine Outbox: dieser Dienst verschickt nichts.
/// </remarks>
public static class GitHubInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Woher Ledger, GitHub und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die GitHub-Datenbank liegt.</param>
    public static IServiceCollection AddGitHubInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<GitHubeinstellungen>(
            configuration.GetSection(GitHubeinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));


        services.AddSingleton(_ => GitHubDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<GitHubDbContext>((provider, options) =>
            GitHubDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IVerbindungsspeicher, EfVerbindungsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<IGitHub, HttpGitHub>();

        services.AddCQRS(typeof(VerbindenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
