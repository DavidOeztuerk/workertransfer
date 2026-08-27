using Girder.Application.Extensions;
using Girder.Data.EntityFrameworkCore.Sessions;
using Girder.Infrastructure.Security.Identity;
using Girder.Passwords.BCrypt;
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
using WorkerTransfer.Identity.Application.Loeschung;
using WorkerTransfer.Identity.Application.Unternehmen;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Verification;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Loeschung;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.Identity.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

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

        services.Configure<Meldeeinstellungen>(
            configuration.GetSection(Meldeeinstellungen.Abschnitt));
        services.Configure<Postsettings>(configuration.GetSection(Postsettings.Abschnitt));

        // bcrypt reads *and* writes while the Python service can still sign
        // people in — Ü-6 in docs/uebergang-python-dotnet.md.
        services.AddBCryptPasswords();

        // After AddSharedInfrastructure, which registers Girder's factory with
        // a plain AddSingleton, so this is the one resolved — Ü-2.
        services.AddSingleton<IPrincipalFactory, UebergangsPrincipalFactory>();
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.TokenValidationParameters.FuerBeideAussteller();
                options.AuchAusDemCookie();
            });

        services.AddSingleton(_ => IdentityDbContextFactory.DataSource(connectionString));
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            IdentityDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddEntityFrameworkRefreshTokens<IdentityDbContext>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IMembershipRepository, EfMembershipRepository>();
        services.AddScoped<ICompanyRepository, EfCompanyRepository>();
        services.AddScoped<IVerificationTokenRepository, EfVerificationTokenRepository>();
        services.AddScoped<UnternehmenAnlegen>();
        services.AddScoped<IInvitationRepository, EfInvitationRepository>();
        services.AddScoped<Firmenzugriff>();
        services.AddScoped<ILoeschbestand, EfLoeschbestand>();

        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));
        services.AddHttpClient(nameof(HttpLoeschzustellung));
        services.AddScoped<IZustellung, HttpLoeschzustellung>();

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
        services.AddScoped<IPostkorb>(anbieter => anbieter.GetRequiredService<Postkorb>());
        services.AddScoped<IPostkorbVersand>(
            anbieter => anbieter.GetRequiredService<Postkorb>());
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<ISessionCapacity, EfSessionCapacity>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccessTokenIssuer, GirderAccessTokenIssuer>();
        services.AddScoped<ISessionService, GirderSessionService>();
        services.AddSingleton<IKorrelation, HttpKorrelation>();

        // The mediator, and the one behaviour Girder does not bring. Registered
        // last so the transaction is the innermost wrapper around a handler:
        // Girder's logging and validation run before anything is written, which
        // is where a rejected command costs nothing.
        services.AddCQRS(typeof(AnmeldenBefehl).Assembly);

        // Order is the point. Sending wraps the transaction, so a confirmation
        // link never reaches an inbox before the row it points at exists.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(VersandBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
