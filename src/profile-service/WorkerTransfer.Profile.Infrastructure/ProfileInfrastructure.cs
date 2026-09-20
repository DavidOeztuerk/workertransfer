using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Profile.Application.Behaviors;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Pruefspur;
using WorkerTransfer.Profile.Infrastructure.Einwilligung;
using WorkerTransfer.Profile.Infrastructure.Entwurf;
using WorkerTransfer.Profile.Infrastructure.Intern;
using WorkerTransfer.Profile.Infrastructure.Loeschung;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Alles, was dieser Dienst für sich entscheidet, steht hier in einem Aufruf —
/// und genauso laut, was <em>nicht</em> hier steht. Es gibt keinen Cache und
/// keinen zu registrieren: nichts implementiert Girders
/// <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide Cache-Behaviors ganz
/// aus der Pipeline. Das ist keine Sparsamkeit, das ist ADR-0013 — ein Widerruf
/// muss beim nächsten Lesen wirken.
/// <para>
/// Und es gibt <strong>keinen Entwurfsanbieter</strong>, solange keiner
/// eingerichtet ist. Ohne Schlüssel verlässt kein Wort dieses Systems das
/// System, und die Oberfläche sagt das, statt es zu verschweigen.
/// </para>
/// </remarks>
public static class ProfileInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger, Entwurf und Geheimnis kommen.</param>
    /// <param name="connectionString">Wo die Profildatenbank liegt.</param>
    public static IServiceCollection AddProfileInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Entwurfseinstellungen>(
            configuration.GetSection(Entwurfseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));
        // Die interne Tuer, durch die scout-service sucht. Leer heisst: zu.
        services.Configure<Meldeeinstellungen>(
            configuration.GetSection(Meldeeinstellungen.Abschnitt));


        services.AddSingleton(_ => ProfileDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<ProfileDbContext>((anbieter, optionen) =>
            ProfileDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProfilspeicher, EfProfilspeicher>();
        services.AddScoped<IPruefspur, EfPruefspur>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();

        // Der Nullentwerfer ist die Voreinstellung, nicht der Rückfall: ohne
        // hinterlegten Schlüssel wird kein Fremddienst gerufen, und der Dienst
        // sagt das, statt eine Vorlage auszugeben, die wie ein Vorschlag
        // aussieht.
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

        services.AddCQRS(typeof(ProfilSpeichernBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration, entwurf);

        return services;
    }

    /// <summary>Was dieser Dienst über sich selbst beantworten kann (ADR-0044).</summary>
    /// <remarks>
    /// <para><strong>Sie stehen hier und nicht in der Dienstgrundlage</strong>,
    /// aus demselben Grund, aus dem der Ledger, die Prüfspur und der Entwerfer
    /// hier stehen: es sind Entscheidungen dieses Dienstes. <em>Welche</em>
    /// vier Felder zum Modell hinausgehen dürfen, ist ADR-0024 §3 für genau
    /// diesen Verbraucher; eine gemeinsame Menge wäre der erste Schritt zu
    /// einem gemeinsamen Prompt mit einem <c>if</c>.</para>
    ///
    /// <para><strong><c>wt.grenze.ziele</c> fehlt hier absichtlich.</strong> Sie
    /// liest nur die Konfiguration, ist damit für jeden Dienst dieselbe, und
    /// steht deshalb in <c>AddNachweis</c> — sie ist die eine, die ein neuer
    /// Dienst nicht vergessen kann.</para>
    /// </remarks>
    private static void Nachweis(
        IServiceCollection services,
        IConfiguration configuration,
        Entwurfseinstellungen entwurf)
    {
        var naht = !string.IsNullOrEmpty(entwurf.Schluessel);

        // DIE FELDMENGE, festgenagelt — und zwar an dem, was laeuft.
        //
        // `EntwurfsgrenzeTests` nagelt dieselbe Menge am BAUM fest und ist
        // damit der schaerfere Waechter: er faellt, bevor etwas ausgeliefert
        // wird. Was er nicht kann, ist im Nachweis STEHEN. Der Mensch, der die
        // Anhang-III-Frage beantworten muss, bekommt hier aufgeschrieben, was
        // dieses System zum Modell hinausschickt — datiert, statt in einem
        // Testprojekt gesucht.
        services.AddScoped<IPruefung>(_ => new Nahtpruefung(
            typeof(Entwurfslage),
            ["Ueberschrift", "Text", "Faehigkeiten", "Wunsch", "Prompt"],
            "für einen Profiltext"));

        services.AddScoped<IPruefung>(_ => new Zahlpruefung(
            [typeof(Profil).Assembly, typeof(ProfilfundV1).Assembly]));

        // Der KI-Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*`
        // aus der Umgebung, einer fuer alle. Der Zugang je Person steht in
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddScoped<IAnbieterquelle>(_ => new Betreiberquelle(
            "anthropic", entwurf.Adresse, naht));

        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        // KEINE AUFZEICHNUNG, und das ist eine Entscheidung (ADR-0024): weder
        // Prompt noch Antwort noch ein Ledger-Eintrag. `null` ist hier also die
        // richtige Antwort und keine fehlende Verdrahtung — wer aufzeichnen
        // muss, setzt `IModellaufzeichnung` um und meldet es an dieser Zeile an.
        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(naht, null));

        // Zwischen der Frage und dem Ledger steht kein weiterer Typ. Das ist
        // die Vorbedingung, an der ADR-0013 in der Praxis scheitert — ein
        // Zwischenspeicher davor faellt niemandem auf, weil alles
        // weiterfunktioniert, nur eben mit dem Stand von vorhin.
        services.AddScoped<IPruefung>(anbieter =>
            new Widerrufspruefung<IEinwilligungstor>(
                anbieter, typeof(HttpEinwilligungstor)));

        var loeschung = new Loescheinstellungen();
        configuration.GetSection(Loescheinstellungen.Abschnitt).Bind(loeschung);

        services.AddScoped<IPruefung>(_ => Loeschpruefung.AlsEmpfaenger(
            "profile",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: true));
    }
}
