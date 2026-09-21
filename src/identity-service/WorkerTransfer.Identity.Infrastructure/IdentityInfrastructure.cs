using Noelia.Application.Extensions;
using Noelia.Data.EntityFrameworkCore.Sessions;
using Noelia.Infrastructure.Security.Identity;
using Noelia.Passwords.BCrypt;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkerTransfer.Outbox;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Application.Behaviors;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Application.Registrierung;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.Identity.Application.Unternehmen;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Verification;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Erasure;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.Identity.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Identity.Infrastructure.Sicherheit;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Identity.Infrastructure.Nachweis;
using WorkerTransfer.ServiceDefaults.Checks;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.Identity.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// The provider packages are named here and nowhere else, so the composition
/// root states which modules the service runs and this states what answers
/// them.
/// </remarks>
public static class IdentityInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="connectionString">Where the identity database lives.</param>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the mail settings come from.</param>
    /// <param name="connectionString">Where the identity database lives.</param>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<NotifySettings>(
            configuration.GetSection(NotifySettings.Section));
        services.Configure<Postsettings>(configuration.GetSection(Postsettings.Section));

        // bcrypt liest UND schreibt. Zur Uebergangszeit musste es das, damit
        // der Python-Dienst dieselben Eintraege noch lesen konnte; jetzt ist es
        // eine freie Entscheidung. Argon2id waere OWASPs erste Wahl und holte
        // jede Person bei ihrer naechsten Anmeldung herueber
        // (`AddArgon2Passwords()` plus `AddBCryptPasswordReader()`) — das ist
        // eine eigene Entscheidung mit eigenem Commit, kein Nebenprodukt des
        // Aufraeumens.
        services.AddBCryptPasswords();


        services.AddSingleton(_ => IdentityDbContextFactory.DataSource(connectionString));
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            IdentityDbContextFactory.Configure(
                options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddDatabaseReadiness<IdentityDbContext>("identity");
        services.AddKeyRing<IdentityDbContext>();

        services.AddEntityFrameworkRefreshTokens<IdentityDbContext>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IMembershipRepository, EfMembershipRepository>();
        services.AddScoped<ICompanyRepository, EfCompanyRepository>();
        services.AddScoped<IVerificationTokenRepository, EfVerificationTokenRepository>();
        services.AddScoped<UnternehmenAnlegen>();
        services.AddScoped<IInvitationRepository, EfInvitationRepository>();
        services.AddScoped<IAccountSettingsStore, EfAccountSettingsStore>();
        services.AddScoped<IPostalAddresses, EfPostalAddresses>();

        // Der Hauptschlüssel wird EINMAL beim Start gelesen, nicht je ResumeRequest:
        // fehlt er, soll der Dienst beim Hochfahren sterben und nicht beim
        // ersten Menschen, der etwas hinterlegen will.
        services.AddSingleton<SecretStore>();
        services.AddScoped<ISecrets, Secrets>();
        services.AddScoped<CompanyAccess>();
        services.AddScoped<IErasureLedger, EfErasureLedger>();

        services.Configure<ErasureSettings>(
            configuration.GetSection(ErasureSettings.Section));
        services.AddHttpClient(nameof(HttpErasureDelivery));
        services.AddScoped<IDelivery, HttpErasureDelivery>();

        // No attempt ceiling. For a notification "leave it after ten" is right;
        // for an erasure it is exactly the silent failure ADR-0027 exists
        // against — a promise nobody redeems, and nobody sees it.
        services.AddOutbox<IdentityDbContext>(
            configuration, einstellungen => einstellungen.HoechsteVersuche = null);
        services.AddSingleton<IEinmaltoken, Sha256Einmaltoken>();
        services.AddSingleton<IVersender, SmtpVersender>();

        // One tray per request, and both ports on it. Queueing and sending are
        // two interfaces so a handler can only ever put something in.
        services.AddScoped<Postkorb>();
        services.AddScoped<IPostkorb>(provider => provider.GetRequiredService<Postkorb>());
        services.AddScoped<IPostkorbVersand>(
            provider => provider.GetRequiredService<Postkorb>());
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<ISessionCapacity, EfSessionCapacity>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccessTokenIssuer, NoeliaAccessTokenIssuer>();
        services.AddScoped<ISessionService, NoeliaSessionService>();
        services.AddSingleton<IKorrelation, HttpKorrelation>();

        // The mediator, and the one behaviour Noelia does not bring. Registered
        // last so the transaction is the innermost wrapper around a handler:
        // Noelia's logging and validation run before anything is written, which
        // is where a rejected command costs nothing.
        services.AddCQRS(typeof(AnmeldenBefehl).Assembly);

        // Order is the point. Sending wraps the transaction, so a confirmation
        // link never reaches an inbox before the row it points at exists.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(VersandBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        Nachweis(services, configuration);

        return services;
    }

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
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(AuditAction).Assembly, typeof(KiZugangV1).Assembly]));

        // DIE EINE ZEILE, DIE KEIN TEST SCHREIBEN KANN.
        //
        // Hier steht die Tabelle, in der jede Person ihren eigenen Anbieter
        // eintraegt — Etikett, Adresse, Modell. `Adr0022Tests` prueft den BAUM;
        // wohin dieser Baum heute Abend spricht, steht allein hier. Gezaehlt
        // wird, genannt wird niemand (ADR-0026), und der verschluesselte
        // Schluessel wird nicht einmal aus der Datenbank geholt.
        services.AddSingleton<IProviderSource, EfProviderSource>();

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // Dieser Dienst fragt selbst kein Modell — aber er haelt den Schalter
        // „Anfragen protokollieren", und den liest in diesem Baum niemand. Das
        // ist genau der Befund, den ein Bildschirmfoto der Kontoseite
        // andersherum erzaehlt, und deshalb steht `gegenstandVorhanden` hier
        // auf wahr.

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        // URSPRUNG und nicht Empfaenger: identity steht nicht in
        // `ErasureRecipient.Fremde`, es IST die Kaskade. Die Frage ist deshalb
        // eine andere — kennt es fuer jeden Empfaenger eine Adresse? Ohne
        // Adresse bekommt der Empfaenger keinen Auftrag, und die Kaskade wartet
        // auf eine Quittung, die niemand angefordert hat.
        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsOrigin(
            "identity",
            ErasureRecipient.Fremde,
            [.. erasure.Adressen
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => entry.Key)]));
    }
}
