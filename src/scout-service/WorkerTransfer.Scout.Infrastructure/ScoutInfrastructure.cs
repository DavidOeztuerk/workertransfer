using Girder.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Behaviors;
using WorkerTransfer.Scout.Application.Kandidaten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Suchen;
using WorkerTransfer.Scout.Infrastructure.Belege;
using WorkerTransfer.Scout.Infrastructure.Benachrichtigung;
using WorkerTransfer.Scout.Infrastructure.Einwilligung;
using WorkerTransfer.Scout.Infrastructure.Entwurf;
using WorkerTransfer.Scout.Infrastructure.Loeschung;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.Scout.Infrastructure.Profile;
using WorkerTransfer.Scout.Infrastructure.Stellen;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Scout.Contracts;

namespace WorkerTransfer.Scout.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// <para>Alles, was dieser Dienst für sich entscheidet, steht hier in einem
/// Aufruf (ADR-0003) — und genauso laut, was <em>nicht</em> hier steht.</para>
///
/// <para><strong>Kein Cache</strong>, und keiner zu registrieren: nichts
/// implementiert Girders <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c>
/// beide Cache-Behaviors ganz aus der Pipeline. Das ist keine Sparsamkeit, das
/// ist ADR-0013 — ein Widerruf muss beim nächsten Lesen wirken. In einem
/// Dienst, der Menschen findet, ist das die wichtigste Zeile, die hier fehlt.</para>
///
/// <para><strong>Kein Hintergrundlauf</strong> ausser der Ausgangsschleife. Es
/// gibt keinen Nachtlauf, der gespeicherte Suchen wiederholt und „neue Treffer"
/// meldet: das wäre der Treffer-Zwischenspeicher mit einem Wecker davor
/// (ADR-0036 Entscheidung 4).</para>
///
/// <para><strong>Kein Postbote und keine Mailadresse.</strong> Der Entwurf einer
/// Ansprache verlässt diesen Dienst über die Antwort an den Browser und auf
/// keinem anderen Weg (Auflage 4).</para>
/// </remarks>
public static class ScoutInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger, Profile, Belege und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Scout-Datenbank liegt.</param>
    public static IServiceCollection AddScoutInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Profileinstellungen>(
            configuration.GetSection(Profileinstellungen.Abschnitt));
        services.Configure<Belegeinstellungen>(
            configuration.GetSection(Belegeinstellungen.Abschnitt));
        services.Configure<Stelleneinstellungen>(
            configuration.GetSection(Stelleneinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        services.AddSingleton(_ => ScoutDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<ScoutDbContext>((anbieter, optionen) =>
            ScoutDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ISuchspeicher, EfSuchspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IProfilsuche, HttpProfilsuche>();
        services.AddScoped<IBelege, HttpBelege>();
        services.AddScoped<IStellen, HttpStellen>();

        // Der Nullentwerfer ist die Voreinstellung, nicht der Rückfall: ohne
        // hinterlegten Schlüssel wird kein Fremddienst gerufen, und der Dienst
        // sagt das, statt eine Vorlage auszugeben, die wie ein Vorschlag
        // aussieht — und die jemand an einen Menschen schickte.
        var entwurf = new Entwurfseinstellungen();
        configuration.GetSection(Entwurfseinstellungen.Abschnitt).Bind(entwurf);

        if (string.IsNullOrEmpty(entwurf.Schluessel))
        {
            services.AddScoped<IEntwerfer, KeinEntwerfer>();
        }
        else
        {
            services.Configure<Entwurfseinstellungen>(
                configuration.GetSection(Entwurfseinstellungen.Abschnitt));
            services.AddScoped<IEntwerfer, HttpEntwerfer>();
        }

        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<ScoutDbContext>(configuration);

        services.AddCQRS(typeof(KandidatenAbfrage).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration, entwurf);

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
        IServiceCollection services,
        IConfiguration configuration,
        Entwurfseinstellungen entwurf)
    {
        var naht = !string.IsNullOrEmpty(entwurf.Schluessel);

        services.AddScoped<IPruefung>(_ => new Zahlpruefung(
            [typeof(Suche).Assembly, typeof(HakenV1).Assembly]));

        services.AddScoped<IPruefung>(_ => new Nahtpruefung(
            typeof(Ansprachelage),
            ["Ueberschrift", "Genannt", "Gesucht", "Wunsch", "Prompt"],
            "für eine Ansprache"));

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
            "scout",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
