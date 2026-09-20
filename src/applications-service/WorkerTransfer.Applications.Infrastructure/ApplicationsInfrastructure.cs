using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Applications.Application.Behaviors;
using WorkerTransfer.Applications.Application.Bewerbungen;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Applications.Infrastructure.Benachrichtigung;
using WorkerTransfer.Applications.Infrastructure.Anschreiben;
using WorkerTransfer.Applications.Infrastructure.Auskunft;
using WorkerTransfer.Applications.Infrastructure.Einwilligung;
using WorkerTransfer.Applications.Infrastructure.Loeschung;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Applications.Infrastructure.Security;
using WorkerTransfer.Applications.Infrastructure.Stellen;
using WorkerTransfer.Applications.Infrastructure.Unternehmen;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Applications.Contracts;

namespace WorkerTransfer.Applications.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Alles, was dieser Dienst für sich entscheidet, steht hier in einem Aufruf —
/// und ebenso laut, was nicht hier steht. Es gibt keinen Zwischenspeicher: die
/// Freigaben schreibt dieser Dienst, er liest sie nirgends, und was er schreibt,
/// muss sofort gelten (ADR-0013). Nichts hier ist ein
/// <c>ICacheableQuery</c>, weshalb <c>AddCQRS</c> beide Zwischenspeicher-Glieder
/// gar nicht erst in die Kette hängt.
/// </remarks>
public static class ApplicationsInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Woher Ledger, Stellen und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Bewerbungsdatenbank liegt.</param>
    public static IServiceCollection AddApplicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Stelleneinstellungen>(
            configuration.GetSection(Stelleneinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<UnternehmensmitgliederEinstellungen>(
            configuration.GetSection(UnternehmensmitgliederEinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));
        services.Configure<Anschreibeneinstellungen>(
            configuration.GetSection(Anschreibeneinstellungen.Abschnitt));
        services.Configure<Auskunftseinstellungen>(
            configuration.GetSection(Auskunftseinstellungen.Abschnitt));


        services.AddSingleton(_ => ApplicationsDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<ApplicationsDbContext>((provider, options) =>
            ApplicationsDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IBewerbungsspeicher, EfBewerbungsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungsschreiber, HttpEinwilligungsschreiber>();
        services.AddScoped<IStellenauskunft, HttpStellenauskunft>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<IEntwurfsspeicher, EfEntwurfsspeicher>();

        // Die eigenen Angaben und der Firmenname — beide über HTTP, beide mit
        // dem Token des Aufrufers. Ein Adapter für zwei Ports, weil er dieselbe
        // Verbindung und dieselben Einstellungen benutzt; zwei Klassen wären
        // zwei Stellen, an denen ein Zeitlimit fehlen kann.
        services.AddScoped<HttpBewerberauskunft>();
        services.AddScoped<IBewerberauskunft>(
            anbieter => anbieter.GetRequiredService<HttpBewerberauskunft>());
        services.AddScoped<IUnternehmensauskunft>(
            anbieter => anbieter.GetRequiredService<HttpBewerberauskunft>());
        services.AddScoped<IKontaktauskunft>(
            anbieter => anbieter.GetRequiredService<HttpBewerberauskunft>());

        services.AddScoped<IUnternehmensmitgliederAbfrage, HttpUnternehmensmitgliederAbfrage>();

        // Der Zugang kommt aus den Kontoeinstellungen der Person (Ollama,
        // MiniMax, Anthropic). Draft__Schluessel bleibt ein Fallback für die
        // Plattform, nicht die einzige Quelle — sonst antwortet Schreiben
        // 503, während die Oberfläche schon einen Anbieter zeigt.
        services.AddScoped<IKiZugangAbfrage, HttpKiZugang>();
        services.AddScoped<IAnschreiber, HttpAnschreiber>();
        services.AddSingleton<AnschreibenArbeiter>();
        services.AddSingleton<IAnschreibenSchlange>(anbieter =>
            anbieter.GetRequiredService<AnschreibenArbeiter>());
        services.AddHostedService(anbieter =>
            anbieter.GetRequiredService<AnschreibenArbeiter>());

        // Vorher ging die Benachrichtigung nach dem Commit als HTTP-Aufruf
        // hinaus, dessen Fehler geschluckt wurde — die Zusage stimmte, aber die
        // Nachricht war dann für immer weg. Jetzt committet die Absicht mit der
        // Änderung, und ein Zusteller bringt sie (ADR-0025). Zehn Versuche, nicht
        // unbegrenzt: nie aufzugeben ist der Löschung vorbehalten, wo ein toter
        // Empfänger die Vollständigkeit blockieren muss.
        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<ApplicationsDbContext>(configuration);

        services.AddCQRS(typeof(BewerbungAbschickenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>Zwei Namen, bei denen <c>quote</c> ein echtes Homonym ist.</summary>
    /// <remarks>
    /// <strong>Öffentlich, weil sie zwei Leser hat</strong>: <c>wt.ki.keine-zahl</c>
    /// zur Laufzeit und <c>WortschatzTests</c> zur Bauzeit. Zwei Listen über
    /// dieselbe Frage laufen auseinander, und die stillere von beiden wüchse.
    /// <para>
    /// <c>Quote</c> ist hier das <em>Zitat</em>: die Stelle im Anschreiben, auf
    /// die sich eine Anmerkung bezieht — wörtlich, damit die Überarbeitung
    /// weiß, wovon die Rede ist. Das deutsche Wort wäre ein Verhältnis und
    /// gehört zu Recht auf die Liste.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Wortausnahme> Wortausnahmen { get; } =
    [
        new("AnmerkenV1.Quote", Zitat),
        new("AnmerkungV1.Quote", Zitat)
    ];

    private const string Zitat =
        "englisch quote = Zitat, die woertliche Stelle im eigenen Anschreiben, "
        + "auf die sich die Anmerkung bezieht; deutsch Quote waere ein "
        + "Verhaeltnis und gehoert auf die Liste";

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
        var anschreiben = new Anschreibeneinstellungen();
        configuration.GetSection(Anschreibeneinstellungen.Abschnitt).Bind(anschreiben);

        // Der Schluessel des BETREIBERS ist hier nur der Rueckfall: den Zugang
        // waehlt die Person in ihren Kontoeinstellungen, und ihn kennt allein
        // identity-service. Was dieser Dienst ueber Anbieter sagen kann, ist
        // deshalb die Haelfte — die andere steht in `identity-service`, gezaehlt.
        var naht = !string.IsNullOrEmpty(anschreiben.Schluessel);

        services.AddScoped<IPruefung>(_ => new Zahlpruefung(
            [typeof(Bewerbungsstand).Assembly, typeof(BewerbungV1).Assembly],
            Wortausnahmen));

        services.AddScoped<IPruefung>(_ => new Nahtpruefung(
            typeof(Anschreibenkontext),
            ["StellenTitel", "Unternehmen", "StellenOrt", "StellenBeschreibung", "GesuchteFaehigkeiten", "EigenerName", "EigeneUeberschrift", "EigenerText", "EigeneFaehigkeiten", "EigenerWerdegang", "Sprache"],
            "für ein Anschreiben"));

        // Der Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*` aus
        // der Umgebung, einer fuer alle. Den Zugang je Person haelt
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddScoped<IAnbieterquelle>(_ => new Betreiberquelle(
            "anthropic", anschreiben.Adresse, naht));

        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        // KEINE AUFZEICHNUNG, und das ist eine Entscheidung (ADR-0024): weder
        // Prompt noch Antwort noch ein Ledger-Eintrag. `null` ist hier die
        // richtige Antwort und keine fehlende Verdrahtung.
        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(naht, null));

        var loeschung = new Loescheinstellungen();
        configuration.GetSection(Loescheinstellungen.Abschnitt).Bind(loeschung);

        services.AddScoped<IPruefung>(_ => Loeschpruefung.AlsEmpfaenger(
            "applications",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
