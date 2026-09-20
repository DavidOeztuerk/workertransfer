using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Ablage;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Anfragen;
using WorkerTransfer.Resume.Application.Behaviors;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Resume.Domain.Pruefspur;
using WorkerTransfer.Resume.Infrastructure.Benachrichtigung;
using WorkerTransfer.Resume.Infrastructure.Einwilligung;
using WorkerTransfer.Resume.Infrastructure.Loeschung;
using WorkerTransfer.Resume.Infrastructure.Persistence;
using WorkerTransfer.Resume.Infrastructure.Security;
using WorkerTransfer.Resume.Infrastructure.Texterkennung;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Pruefungen;
using WorkerTransfer.Resume.Contracts;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Resume.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here, in one call — and,
/// just as loudly, what is not here. There is no cache: the release a company
/// holds is read from the ledger on every request, and a cache would mean a
/// withdrawal takes effect whenever the cache feels like it (ADR-0013). Nothing
/// implements Noelia's <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both
/// cache behaviours out of the pipeline entirely.
/// </remarks>
public static class ResumeInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the ledger and the secrets come from.</param>
    /// <param name="connectionString">Where the resume database lives.</param>
    public static IServiceCollection AddResumeInfrastructure(
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


        services.AddSingleton(_ => ResumeDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ResumeDbContext>((provider, options) =>
            ResumeDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ILebenslaufSpeicher, EfLebenslaufSpeicher>();
        services.AddScoped<IAnfragenSpeicher, EfAnfragenSpeicher>();
        services.AddScoped<IUnterlagenSpeicher, EfUnterlagenSpeicher>();
        services.AddScoped<IFundSpeicher, EfFundSpeicher>();

        // DER TEXTERKENNER — eine Bibliothek im Prozess, kein Dienst im Netz
        // (ADR-0043). Hier steht die Entscheidung, und hier waere sie zu
        // aendern: wer jemals einen fremden Erkenner einsetzt, schreibt dessen
        // Adresse in dieselbe Zeile in die Konfiguration — sonst weist die
        // Souveraenitaetsgrenze den Aufruf ab, ohne eine Zeile zu
        // protokollieren, und der Knopf tut einfach nichts.
        services.AddSingleton<ITexterkennung, PdfTexterkennung>();

        // Die Ablage aus ADR-0021, zurueck in .NET (ADR-0035). Sie steht hier
        // und nicht in `ServiceDefaults`: ob ein Dienst Dateien haelt, ist eine
        // Entscheidung, und Entscheidungen gehoeren in den Kompositionswurzel
        // des Dienstes, der sie trifft (ADR-0003).
        services.AddAblage(configuration);
        services.AddScoped<IPruefspur, EfPruefspur>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<HttpKorrelation>();
        services.AddScoped<IKorrelation>(a => a.GetRequiredService<HttpKorrelation>());
        services.AddScoped<IKorrelationsanhang>(a => a.GetRequiredService<HttpKorrelation>());

        // The old path lost a notification whenever the HTTP call failed after
        // the commit — the failure was swallowed, and nobody learned that their
        // request was answered. Now the intent commits with the domain change
        // and a dispatcher delivers it (ADR-0025). Ten attempts, not unlimited:
        // never giving up is reserved for an erasure, where a dead recipient
        // must block the completion.
        services.AddScoped<IZustellung, HttpBenachrichtigung>();
        services.AddOutbox<ResumeDbContext>(configuration);

        services.AddCQRS(typeof(LebenslaufAnfragenBefehl).Assembly);
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
        services.AddSingleton<ISecurityCheck>(_ => new Zahlpruefung(
            [typeof(Anfragestand).Assembly, typeof(StationV1).Assembly]));

        // Keine KI-Naht: `Anbieterpruefung` ohne Quelle meldet NichtAnwendbar,
        // und das ist ausdruecklich KEIN gruener Haken — ein Haken an etwas,
        // das hier gar nicht gilt, waere Rauschen in dem Dokument, das Rauschen
        // durchschneiden soll.
        services.AddSingleton<ISecurityCheck>(anbieter => new Anbieterpruefung(
            anbieter.GetServices<IAnbieterquelle>()));


        // Zwischen der Frage und dem Ledger steht kein weiterer Typ — die
        // Vorbedingung, an der ADR-0013 in der Praxis scheitert. Ein
        // Zwischenspeicher davor faellt niemandem auf, weil alles
        // weiterfunktioniert, nur eben mit dem Stand von vorhin.
        services.AddSingleton<ISecurityCheck>(anbieter =>
            new Widerrufspruefung<IEinwilligungstor>(
                anbieter, typeof(HttpEinwilligungstor)));

        var loeschung = new Loescheinstellungen();
        configuration.GetSection(Loescheinstellungen.Abschnitt).Bind(loeschung);

        services.AddSingleton<ISecurityCheck>(_ => Loeschpruefung.AlsEmpfaenger(
            "resume",
            !string.IsNullOrEmpty(loeschung.Geheimnis),
            pruefspurVorhanden: true));
    }
}
