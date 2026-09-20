using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Jobs.Application.Behaviors;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Stellen;
using WorkerTransfer.Jobs.Domain.Stellen;
using WorkerTransfer.Jobs.Infrastructure.Entwurf;
using WorkerTransfer.Jobs.Infrastructure.Persistence;
using WorkerTransfer.Jobs.Infrastructure.Rueckzug;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Jobs.Contracts;

namespace WorkerTransfer.Jobs.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Was hier <em>nicht</em> steht, ist so wichtig wie was hier steht: kein
/// Consent-Tor, weil dieser Dienst nichts über Menschen hält, und keine
/// Outbox — es gibt keine Absicht zu verschicken.
/// </remarks>
public static class JobsInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Entwurf und Geheimnis kommen.</param>
    /// <param name="connectionString">Wo die Stellendatenbank liegt.</param>
    public static IServiceCollection AddJobsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Entwurfseinstellungen>(
            configuration.GetSection(Entwurfseinstellungen.Abschnitt));
        services.Configure<Rueckzugseinstellungen>(
            configuration.GetSection(Rueckzugseinstellungen.Abschnitt));


        services.AddSingleton(_ => JobsDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<JobsDbContext>((anbieter, optionen) =>
            JobsDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IStellenspeicher, EfStellenspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Ohne hinterlegten Schlüssel wird kein Fremddienst gerufen. Das ist
        // die Voreinstellung und kein Rückfall.
        var entwurf = new Entwurfseinstellungen();
        configuration.GetSection(Entwurfseinstellungen.Abschnitt).Bind(entwurf);

        if (string.IsNullOrEmpty(entwurf.Schluessel))
        {
            services.AddScoped<IEntwerfer, KeinEntwerfer>();
        }
        else
        {
            services.AddScoped<IEntwerfer, HttpEntwerfer>();
        }

        services.AddCQRS(typeof(StelleAnlegenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, entwurf);

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
        IServiceCollection services, Entwurfseinstellungen entwurf)
    {
        var naht = !string.IsNullOrEmpty(entwurf.Schluessel);

        services.AddScoped<IPruefung>(_ => new Zahlpruefung(
            [typeof(Faehigkeitenliste).Assembly, typeof(StelleV1).Assembly]));

        services.AddScoped<IPruefung>(_ => new Nahtpruefung(
            typeof(Anzeigenentwurf),
            ["Titel", "Beschreibung", "Faehigkeiten", "Ort", "Wunsch", "Prompt"],
            "für eine Stellenanzeige"));

        // Der Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*` aus
        // der Umgebung, einer fuer alle. Den Zugang je Person haelt
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddScoped<IAnbieterquelle>(_ => new Betreiberquelle(
            "anthropic", entwurf.Adresse, naht));

        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        // KEINE AUFZEICHNUNG, und das ist eine Entscheidung (ADR-0024): weder
        // Prompt noch Antwort noch ein Ledger-Eintrag. `null` ist hier die
        // richtige Antwort und keine fehlende Verdrahtung.
        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(
            nahtVorhanden: true, anbieterEingerichtet: naht, null));

        // Kein Empfaenger: eine Anzeige gehoert einem Unternehmen, nicht einem Menschen (ADR-0027 §2). Der Firmenrueckzug ist ein eigener Vertrag und zaehlt ausdruecklich nicht zur Vollstaendigkeit einer Loeschung.
        services.AddScoped<IPruefung>(_ => Loeschpruefung.OhneZeilen("jobs"));
    }
}
