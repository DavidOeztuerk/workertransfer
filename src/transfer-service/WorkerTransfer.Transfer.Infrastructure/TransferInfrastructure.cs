using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Behaviors;
using WorkerTransfer.Transfer.Application.Markt;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;
using WorkerTransfer.Transfer.Infrastructure.Benachrichtigung;
using WorkerTransfer.Transfer.Infrastructure.Einwilligung;
using WorkerTransfer.Transfer.Infrastructure.Loeschung;
using WorkerTransfer.Transfer.Infrastructure.Persistence;
using WorkerTransfer.Transfer.Infrastructure.Security;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Transfer.Contracts;

namespace WorkerTransfer.Transfer.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Kein Zwischenspeicher — und bei diesem Dienst wiegt das am schwersten: was
/// hier freigegeben wird, ist die Aussage „ich höre zu", und ein Widerruf muss
/// beim allernächsten Lesezugriff wirken (ADR-0013). Nichts hier ist ein
/// <c>ICacheableQuery</c>, weshalb <c>AddCQRS</c> beide
/// Zwischenspeicher-Glieder gar nicht erst in die Kette hängt.
/// </remarks>
public static class TransferInfrastructure
{
    /// <param name="services">Der Behälter.</param>
    /// <param name="configuration">Woher Ledger und Geheimnisse kommen.</param>
    /// <param name="connectionString">Wo die Transferdatenbank liegt.</param>
    public static IServiceCollection AddTransferInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Benachrichtigungseinstellungen>(
            configuration.GetSection(Benachrichtigungseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));


        services.AddSingleton(_ => TransferDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<TransferDbContext>((provider, options) =>
            TransferDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IMarktspeicher, EfMarktspeicher>();
        services.AddScoped<IAnfragenspeicher, EfAnfragenspeicher>();
        services.AddScoped<ITransferspeicher, EfTransferspeicher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();

        // Die Absicht committet mit der Änderung, ein Zusteller bringt sie
        // (ADR-0025). Zehn Versuche, nicht unbegrenzt: nie aufzugeben ist der
        // Löschung vorbehalten, wo ein toter Empfänger die Vollständigkeit
        // blockieren muss.
        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<TransferDbContext>(configuration);

        services.AddCQRS(typeof(MarktstatusSichernBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>Vier Namen, bei denen <c>note</c> ein echtes Homonym ist.</summary>
    /// <remarks>
    /// <strong>Öffentlich, weil sie zwei Leser hat</strong>: <c>wt.ki.keine-zahl</c>
    /// zur Laufzeit und <c>WortschatzTests</c> zur Bauzeit.
    /// <para>
    /// Es ist überall die <em>Anmerkung</em> — was eine Person zu ihrem
    /// Marktstatus schreibt, und was ein Unternehmen zu seinem Angebot
    /// dazusagt. Das deutsche Wort wäre die gefährlichste Vokabel, die eine
    /// Vermittlungsplattform haben kann, und steht deshalb auf der Liste.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Wortausnahme> Wortausnahmen { get; } =
    [
        new("MarktstatusV1.Note", Anmerkung),
        new("MarktstatusSchreibenV1.Note", Anmerkung),
        new("AngebotV1.Note", Anmerkung),
        new("TransferV1.OfferNote", Anmerkung)
    ];

    private const string Anmerkung =
        "englisch note = Anmerkung — was die Person zu ihrem Marktstatus "
        + "schreibt, beziehungsweise was ein Unternehmen zu seinem Angebot "
        + "dazusagt; deutsch Note waere eine Bewertung und ist hier nicht gemeint";

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
            [typeof(Anfragestand).Assembly, typeof(MarktstatusV1).Assembly],
            Wortausnahmen));

        // Keine KI-Naht: `Anbieterpruefung` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddScoped<IPruefung>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));

        services.AddScoped<IPruefung>(_ => new Aufzeichnungspruefung(false, false, null));

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
            "transfer",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: false));
    }
}
