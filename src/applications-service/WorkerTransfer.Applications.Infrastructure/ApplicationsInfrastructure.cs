using Noelia.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Applications.Application.Behaviors;
using WorkerTransfer.Applications.Application.Applications;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Benachrichtigung;
using WorkerTransfer.Applications.Infrastructure.Anschreiben;
using WorkerTransfer.Applications.Infrastructure.Auskunft;
using WorkerTransfer.Applications.Infrastructure.Einwilligung;
using WorkerTransfer.Applications.Infrastructure.Erasure;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Applications.Infrastructure.Security;
using WorkerTransfer.Applications.Infrastructure.Jobs;
using WorkerTransfer.Applications.Infrastructure.Unternehmen;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Applications.Contracts;
using Noelia.Abstractions.Security.Checks;

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
    /// <param name="configuration">Woher Ledger, Jobs und Secrets kommen.</param>
    /// <param name="connectionString">Wo die Bewerbungsdatenbank liegt.</param>
    public static IServiceCollection AddApplicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<JobSettings>(
            configuration.GetSection(JobSettings.Section));
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.Section));
        services.Configure<CompanyMemberSettings>(
            configuration.GetSection(CompanyMemberSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));
        services.Configure<CoverLetterSettings>(
            configuration.GetSection(CoverLetterSettings.Section));
        services.Configure<LookupSettings>(
            configuration.GetSection(LookupSettings.Section));


        services.AddSingleton(_ => ApplicationsDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ApplicationsDbContext>((provider, options) =>
            ApplicationsDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ApplicationsDbContext>("applications");
        services.AddKeyRing<ApplicationsDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IApplicationStore, EfApplicationStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();
        services.AddScoped<IConsentWriter, HttpConsentWriter>();
        services.AddScoped<IJobLookup, HttpJobLookup>();
        services.AddScoped<IAufrufertoken, HttpAufrufertoken>();
        services.AddScoped<IDraftStore, EfDraftStore>();

        // Die eigenen Angaben und der Firmenname — beide über HTTP, beide mit
        // dem Token des Aufrufers. Ein Adapter für zwei Ports, weil er dieselbe
        // Connection und dieselben Einstellungen benutzt; zwei Klassen wären
        // zwei Jobs, an denen ein Zeitlimit fehlen kann.
        services.AddScoped<HttpApplicantLookup>();
        services.AddScoped<IApplicantLookup>(
            provider => provider.GetRequiredService<HttpApplicantLookup>());
        services.AddScoped<IUnternehmensauskunft>(
            provider => provider.GetRequiredService<HttpApplicantLookup>());
        services.AddScoped<IKontaktauskunft>(
            provider => provider.GetRequiredService<HttpApplicantLookup>());

        services.AddScoped<ICompanyMemberLookup, HttpCompanyMemberLookup>();

        // Der Zugang kommt aus den AccountSettings der Person (Ollama,
        // MiniMax, Anthropic). Draft__Schluessel bleibt ein Fallback für die
        // Plattform, nicht die einzige Quelle — sonst antwortet Schreiben
        // 503, während die Oberfläche schon einen Anbieter zeigt.
        services.AddScoped<IKiZugangAbfrage, HttpKiZugang>();
        services.AddScoped<ICoverLetterWriter, HttpCoverLetterWriter>();
        services.AddSingleton<CoverLetterWorker>();
        services.AddSingleton<ICoverLetterQueue>(provider =>
            provider.GetRequiredService<CoverLetterWorker>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<CoverLetterWorker>());

        // Vorher ging die Benachrichtigung nach dem Commit als HTTP-Aufruf
        // hinaus, dessen Fehler geschluckt wurde — die Zusage stimmte, aber die
        // Nachricht war dann für immer weg. Jetzt committet die Absicht mit der
        // Änderung, und ein Zusteller bringt sie (ADR-0025). Zehn Versuche, nicht
        // unbegrenzt: nie aufzugeben ist der Löschung vorbehalten, wo ein toter
        // Empfänger die Vollständigkeit blockieren muss.
        services.AddScoped<IDelivery, HttpBenachrichtigung>();
        services.AddOutbox<ApplicationsDbContext>(configuration);

        services.AddCQRS(typeof(SubmitApplicationCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

    /// <summary>Zwei Namen, bei denen <c>quote</c> ein echtes Homonym ist.</summary>
    /// <remarks>
    /// <strong>Öffentlich, weil sie zwei Leser hat</strong>: <c>wt.ki.keine-zahl</c>
    /// zur Laufzeit und <c>VocabularyTests</c> zur Bauzeit. Zwei Listen über
    /// dieselbe Frage laufen auseinander, und die stillere von beiden wüchse.
    /// <para>
    /// <c>Quote</c> ist hier das <em>Zitat</em>: die Job im Anschreiben, auf
    /// die sich eine Anmerkung bezieht — wörtlich, damit die Überarbeitung
    /// weiß, wovon die Rede ist. Das deutsche Wort wäre ein Verhältnis und
    /// gehört zu Recht auf die Liste.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TermException> Wortausnahmen { get; } =
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
    /// <para>Sie stehen hier und nicht in der ServiceFoundation, aus demselben
    /// Grund, aus dem der Ledger, die Prüfspur und der Speicher hier stehen: es
    /// sind Entscheidungen <em>dieses</em> Dienstes.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> fehlt hier absichtlich — sie liest nur die
    /// Konfiguration, ist damit für jeden Dienst dieselbe und steht in
    /// <c>AddEvidence</c>. Sie ist die eine, die ein neuer Dienst nicht
    /// vergessen kann.</para>
    /// </remarks>
    private static void Nachweis(
        IServiceCollection services, IConfiguration configuration)
    {
        var anschreiben = new CoverLetterSettings();
        configuration.GetSection(CoverLetterSettings.Section).Bind(anschreiben);

        // Der Schluessel des BETREIBERS ist hier nur der Rueckfall: den Zugang
        // waehlt die Person in ihren AccountSettings, und ihn kennt allein
        // identity-service. Was dieser Dienst ueber Anbieter sagen kann, ist
        // deshalb die Haelfte — die andere steht in `identity-service`, gezaehlt.
        var naht = !string.IsNullOrEmpty(anschreiben.Schluessel);

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(ApplicationState).Assembly, typeof(ApplicationV1).Assembly],
            Wortausnahmen));

        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(CoverLetterContext),
            ["StellenTitel", "Unternehmen", "StellenOrt", "StellenBeschreibung", "GesuchteFaehigkeiten", "EigenerName", "EigeneUeberschrift", "EigenerText", "EigeneFaehigkeiten", "EigenerWerdegang", "Sprache"],
            "für ein Anschreiben"));

        // Der Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*` aus
        // der EnvironmentFile, einer fuer alle. Den Zugang je Person haelt
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            "anthropic", anschreiben.Adresse, naht));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "applications",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            auditTrailPresent: false));
    }
}
