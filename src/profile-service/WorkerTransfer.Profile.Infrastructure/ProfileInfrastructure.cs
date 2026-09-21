using Noelia.Application.Extensions;
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
using WorkerTransfer.Profile.Infrastructure.Erasure;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Checks;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.ServiceDefaults;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Profile.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Alles, was dieser Dienst für sich entscheidet, steht hier in einem Aufruf —
/// und genauso laut, was <em>nicht</em> hier steht. Es gibt keinen Cache und
/// keinen zu registrieren: nichts implementiert Noelias
/// <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide Cache-Behaviors ganz
/// aus der Pipeline. Das ist keine Sparsamkeit, das ist ADR-0013 — ein Revocation
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

        services.Configure<ConsentSettings>(
            configuration.GetSection(ConsentSettings.Section));
        services.Configure<DraftSettings>(
            configuration.GetSection(DraftSettings.Section));
        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));
        // Die interne Tuer, durch die scout-service sucht. Leer heisst: zu.
        services.Configure<NotifySettings>(
            configuration.GetSection(NotifySettings.Section));


        services.AddSingleton(_ => ProfileDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ProfileDbContext>((provider, optionen) =>
            ProfileDbContextFactory.Configure(
                optionen, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<ProfileDbContext>("profile");
        services.AddKeyRing<ProfileDbContext>();

        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProfileStore, EfProfileStore>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IConsentGate, HttpConsentGate>();

        // Der Nullentwerfer ist die Voreinstellung, nicht der Rückfall: ohne
        // hinterlegten Schlüssel wird kein Fremddienst gerufen, und der Dienst
        // sagt das, statt eine ResumeTemplate auszugeben, die wie ein Vorschlag
        // aussieht.
        var draft = new DraftSettings();
        configuration.GetSection(DraftSettings.Section).Bind(draft);

        if (string.IsNullOrEmpty(draft.Schluessel))
        {
            services.AddScoped<IDrafter, NoDrafter>();
        }
        else
        {
            services.AddScoped<IDrafter, HttpDrafter>();
        }

        services.AddCQRS(typeof(StoreProfileCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration, draft);

        return services;
    }

    /// <summary>Was dieser Dienst über sich selbst beantworten kann (ADR-0044).</summary>
    /// <remarks>
    /// <para><strong>Sie stehen hier und nicht in der ServiceFoundation</strong>,
    /// aus demselben Grund, aus dem der Ledger, die Prüfspur und der Entwerfer
    /// hier stehen: es sind Entscheidungen dieses Dienstes. <em>Welche</em>
    /// vier Felder zum Modell hinausgehen dürfen, ist ADR-0024 §3 für genau
    /// diesen Verbraucher; eine gemeinsame Menge wäre der erste Schritt zu
    /// einem gemeinsamen Prompt mit einem <c>if</c>.</para>
    ///
    /// <para><strong><c>wt.grenze.ziele</c> fehlt hier absichtlich.</strong> Sie
    /// liest nur die Konfiguration, ist damit für jeden Dienst dieselbe, und
    /// steht deshalb in <c>AddEvidence</c> — sie ist die eine, die ein neuer
    /// Dienst nicht vergessen kann.</para>
    /// </remarks>
    private static void Nachweis(
        IServiceCollection services,
        IConfiguration configuration,
        DraftSettings draft)
    {
        var naht = !string.IsNullOrEmpty(draft.Schluessel);

        // DIE FELDMENGE, festgenagelt — und zwar an dem, was laeuft.
        //
        // `EntwurfsgrenzeTests` nagelt dieselbe Menge am BAUM fest und ist
        // damit der schaerfere Waechter: er faellt, bevor etwas ausgeliefert
        // wird. Was er nicht kann, ist im Nachweis STEHEN. Der Mensch, der die
        // Anhang-III-Frage beantworten muss, bekommt hier aufgeschrieben, was
        // dieses System zum Modell hinausschickt — datiert, statt in einem
        // Testprojekt gesucht.
        services.AddSingleton<ISecurityCheck>(_ => new SeamCheck(
            typeof(DraftSituation),
            ["Ueberschrift", "Text", "Faehigkeiten", "Wunsch", "Prompt"],
            "für einen Profiltext"));

        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(PersonProfile).Assembly, typeof(ProfileHitV1).Assembly]));

        // Der KI-Anbieter dieses Dienstes ist der des BETREIBERS: `Draft__*`
        // aus der EnvironmentFile, einer fuer alle. Der Zugang je Person steht in
        // identity-service, und dort wird gezaehlt statt genannt.
        services.AddSingleton<IProviderSource>(_ => new OperatorProviderSource(
            "anthropic", draft.Adresse, naht));

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));


        // Zwischen der Frage und dem Ledger steht kein weiterer Typ. Das ist
        // die Vorbedingung, an der ADR-0013 in der Praxis scheitert — ein
        // Zwischenspeicher davor faellt niemandem auf, weil alles
        // weiterfunktioniert, nur eben mit dem State von vorhin.
        services.AddSingleton<ISecurityCheck>(provider =>
            new RevocationCheck<IConsentGate>(
                provider, typeof(HttpConsentGate)));

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsRecipient(
            "profile",
            !string.IsNullOrEmpty(erasure.Geheimnis),
            auditTrailPresent: true));
    }
}
