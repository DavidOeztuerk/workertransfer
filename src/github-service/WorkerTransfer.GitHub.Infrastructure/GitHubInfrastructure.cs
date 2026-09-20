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
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.GitHub.Contracts;

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
        services.Configure<GitHubAnmeldeeinstellungen>(
            configuration.GetSection(GitHubAnmeldeeinstellungen.Abschnitt));
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
        // Ohne Zugangsdaten meldet sie sich als „nicht eingerichtet", und der
        // Gist bleibt der Weg. Registriert wird sie trotzdem — ein Dienst, der
        // je nach Konfiguration andere Abhängigkeiten hat, fällt erst beim
        // ersten Aufruf um statt beim Start.
        services.AddScoped<IGitHubAnmeldung, HttpGitHubAnmeldung>();

        services.AddCQRS(typeof(VerbindenBefehl).Assembly);
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
            [typeof(Loginfehler).Assembly, typeof(RepositoryV1).Assembly]));

        // Keine KI-Naht: `Anbieterpruefung` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(false, null));

        // Zwischen der Frage und dem Ledger steht kein weiterer Typ — die
        // Vorbedingung, an der ADR-0013 in der Praxis scheitert. Ein
        // Zwischenspeicher davor faellt niemandem auf, weil alles
        // weiterfunktioniert, nur eben mit dem Stand von vorhin.
        services.AddScoped<IPruefung>(anbieter =>
            new Widerrufspruefung<IEinwilligungstor>(
                anbieter, typeof(HttpEinwilligungstor)));

        var loeschung = new Loescheinstellungen();
        configuration.GetSection(Loescheinstellungen.Abschnitt).Bind(loeschung);

        services.AddScoped<IPruefung>(_ => Loeschpruefung.AlsEmpfaenger(
            "github",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
