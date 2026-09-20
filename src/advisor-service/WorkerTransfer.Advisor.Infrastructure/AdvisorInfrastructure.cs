using Girder.Application.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Advisor.Application.Behaviors;
using WorkerTransfer.Advisor.Application.Gespraeche;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandate;
using WorkerTransfer.Advisor.Infrastructure.Auskunft;
using WorkerTransfer.Advisor.Infrastructure.Benachrichtigung;
using WorkerTransfer.Advisor.Infrastructure.Einwilligung;
using WorkerTransfer.Advisor.Infrastructure.Loeschung;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.Advisor.Infrastructure.Sicherheit;
using WorkerTransfer.Advisor.Infrastructure.Uebergabe;
using WorkerTransfer.Outbox;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Advisor.Contracts;

namespace WorkerTransfer.Advisor.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// <para>Alles, was dieser Dienst für sich entscheidet, steht hier in einem
/// Aufruf (ADR-0003) — und genauso laut, was <em>nicht</em> hier steht.</para>
///
/// <para><strong>Kein Cache</strong>, und keiner zu registrieren: nichts
/// implementiert Girders <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide
/// Cache-Behaviors ganz aus der Pipeline. Das ist keine Sparsamkeit, das ist
/// ADR-0013 — und in einem Dienst, dessen ganzer Zweck gestufte Sichtbarkeit
/// ist, die wichtigste Zeile, die hier fehlt.</para>
///
/// <para><strong>Kein Hintergrundlauf</strong> ausser der Ausgangsschleife. Es
/// gibt keinen Nachtlauf, der Stufen wieder öffnet, keine Erinnerung an ein
/// stehengebliebenes Gespräch und keinen Dienst, der für die Person antwortet.</para>
///
/// <para><strong>Kein Entwerfer.</strong> ADR-0037 Entscheidung 5 lässt die
/// KI-Naht offen, bis feststeht, dass Menschen hier selbst schreiben. Solange
/// das offen ist, gibt es keinen Port dafür — und damit auch keine Frage, was
/// ein Modell über einen Menschen sagen dürfte.</para>
/// </remarks>
public static class AdvisorInfrastructure
{
    /// <param name="services">Der Container.</param>
    /// <param name="configuration">Woher Ledger, Auskünfte und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Berater-Datenbank liegt.</param>
    public static IServiceCollection AddAdvisorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Auskunftseinstellungen>(
            configuration.GetSection(Auskunftseinstellungen.Abschnitt));
        services.Configure<Uebergabeeinstellungen>(
            configuration.GetSection(Uebergabeeinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        services.AddSingleton(_ => AdvisorDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<AdvisorDbContext>((anbieter, optionen) =>
            AdvisorDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<IMandatspeicher, EfMandatspeicher>();
        services.AddScoped<IGespraechsspeicher, EfGespraechsspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IVorgangsuebergabe, HttpVorgangsuebergabe>();

        // Eine Klasse, zwei Ports: dieselbe Tür, zwei Fragen. Getrennt
        // registriert, damit ein Handler nur die sieht, die er braucht.
        services.AddScoped<HttpAuskuenfte>();
        services.AddScoped<IFirmenauskunft>(a => a.GetRequiredService<HttpAuskuenfte>());
        services.AddScoped<IPersonenauskunft>(a => a.GetRequiredService<HttpAuskuenfte>());

        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<AdvisorDbContext>(configuration);

        services.AddCQRS(typeof(GespraechEroeffnenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>
    /// Sieben Namen, bei denen ein Wort der Verbotsliste ein echtes Homonym ist.
    /// </summary>
    /// <remarks>
    /// <para><strong>Öffentlich, weil sie zwei Leser hat.</strong>
    /// <c>wt.ki.keine-zahl</c> liest sie zur Laufzeit, <c>WortschatzTests</c>
    /// zur Bauzeit. Zwei Listen über dieselbe Frage laufen auseinander, und
    /// die stillere von beiden wüchse — hier wächst keine: jede Zeile steht im
    /// Befund, den ein Betriebsrat liest.</para>
    ///
    /// <para><c>WorkloadPercent</c> ist das Pensum, das die Person selbst für
    /// ihre nächste Stelle nennt — eine Angabe über eine <em>Stelle</em>.
    /// <c>Note</c> ist englisch und heißt Anmerkung: der erste Satz eines
    /// Unternehmens an einen Menschen. Das deutsche Wort wäre eine Bewertung,
    /// und genau deshalb steht es auf der Liste — hier ist es nicht gemeint.</para>
    ///
    /// <para>Dieselbe Form halten die drei Sprachkataloge der Oberfläche für
    /// echte Kognaten („Status", „Website", „Administrator"): kurz, und jede
    /// Zeile ein Einzelfall.</para>
    /// </remarks>
    public static IReadOnlyList<Wortausnahme> Wortausnahmen { get; } =
    [
        new("MandatV1.WorkloadPercent", Pensum),
        new("MandatSchreibenV1.WorkloadPercent", Pensum),
        new("GespraechV1.WorkloadPercent", Pensum),
        new("Mandat.PensumProzent", Pensum),
        new("GespraechEroeffnenV1.Note", Anmerkung),
        new("GespraechV1.Note", Anmerkung),
        new("MeinGespraechV1.Note", Anmerkung)
    ];

    private const string Pensum =
        "das Pensum, das die Person SELBST fuer ihre naechste Stelle nennt — "
        + "eine Angabe ueber eine Stelle, keine ueber einen Menschen";

    private const string Anmerkung =
        "englisch note = Anmerkung, der erste Satz eines Unternehmens an einen "
        + "Menschen; deutsch Note waere eine Bewertung, und genau deshalb steht "
        + "das Wort auf der Liste — hier ist es nicht gemeint";

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
            [typeof(UebergangNichtErlaubt).Assembly, typeof(MandatV1).Assembly],
            Wortausnahmen));

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
            "advisor",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
