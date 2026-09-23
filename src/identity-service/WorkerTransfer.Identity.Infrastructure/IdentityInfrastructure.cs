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
using WorkerTransfer.Identity.Application.SignIn;
using WorkerTransfer.Identity.Application.Behaviors;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Application.Registration;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.Identity.Application.Companies;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Verification;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Erasure;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Identity.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Identity.Infrastructure.Evidence;
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
        services.Configure<MailSettings>(configuration.GetSection(MailSettings.Section));

        // bcrypt reads AND writes. During the migration it had to, so the
        // Python service could still read the same entries; now it is a free
        // decision. Argon2id would be OWASP's first choice and would carry every
        // person over at their next sign-in (`AddArgon2Passwords()` plus
        // `AddBCryptPasswordReader()`) — that is a decision of its own with a
        // commit of its own, not a by-product of tidying up.
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
        services.AddScoped<CreateCompany>();
        services.AddScoped<IInvitationRepository, EfInvitationRepository>();
        services.AddScoped<IAccountSettingsStore, EfAccountSettingsStore>();
        services.AddScoped<IPostalAddresses, EfPostalAddresses>();

        // The master key is read ONCE at startup, not per request: if it is
        // missing the service should die while coming up and not at the first
        // human being who wants to store something.
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
            configuration, settings => settings.HoechsteVersuche = null);
        services.AddSingleton<IOneTimeToken, Sha256OneTimeToken>();
        services.AddSingleton<IMailSender, SmtpMailSender>();

        // One tray per request, and both ports on it. Queueing and sending are
        // two interfaces so a handler can only ever put something in.
        services.AddScoped<MailTray>();
        services.AddScoped<IMailTray>(provider => provider.GetRequiredService<MailTray>());
        services.AddScoped<IMailTrayDispatch>(
            provider => provider.GetRequiredService<MailTray>());
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<ISessionCapacity, EfSessionCapacity>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccessTokenIssuer, NoeliaAccessTokenIssuer>();
        services.AddScoped<ISessionService, NoeliaSessionService>();
        services.AddSingleton<ICorrelation, HttpCorrelation>();

        // The mediator, and the one behaviour Noelia does not bring. Registered
        // last so the transaction is the innermost wrapper around a handler:
        // Noelia's logging and validation run before anything is written, which
        // is where a rejected command costs nothing.
        services.AddCQRS(typeof(SignInCommand).Assembly);

        // Order is the point. Sending wraps the transaction, so a confirmation
        // link never reaches an inbox before the row it points at exists.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(DispatchBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        Evidence(services, configuration);

        return services;
    }

    /// <summary>What this service can answer about itself (ADR-0044).</summary>
    /// <remarks>
    /// <para>They stand here and not in the ServiceFoundation, for the same
    /// reason the ledger, the audit trail and the store stand here: they are
    /// decisions of <em>this</em> service.</para>
    ///
    /// <para><c>wt.grenze.ziele</c> is deliberately missing here — it reads
    /// nothing but the configuration, is therefore the same for every service,
    /// and stands in <c>AddEvidence</c>. It is the one a new service cannot
    /// forget.</para>
    /// </remarks>
    private static void Evidence(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISecurityCheck>(_ => new NumberCheck(
            [typeof(AuditAction).Assembly, typeof(KiZugangV1).Assembly]));

        // THE ONE LINE NO TEST CAN WRITE.
        //
        // Here stands the table in which every person enters their own provider
        // — label, address, model. `Adr0022Tests` checks the TREE; where this
        // tree speaks tonight stands here and nowhere else. What happens is
        // counting, and nobody is named (ADR-0026), and the encrypted key is not
        // even fetched from the database.
        services.AddSingleton<IProviderSource, EfProviderSource>();

        services.AddSingleton<ISecurityCheck>(provider => new ProviderCheck(
            provider.GetServices<IProviderSource>()));

        // This service asks no model itself — but it holds the switch "log
        // requests", and nobody in this tree reads it. That is exactly the
        // finding a screenshot of the account page tells the other way round,
        // and that is why the check is registered here and not left out.

        var erasure = new ErasureSettings();
        configuration.GetSection(ErasureSettings.Section).Bind(erasure);

        // ORIGIN and not recipient: identity does not stand in
        // `ErasureRecipient.Fremde`, it IS the cascade. So the question is a
        // different one — does it know an address for every recipient? Without
        // an address the recipient gets no order, and the cascade waits for a
        // receipt nobody asked for.
        services.AddSingleton<ISecurityCheck>(_ => ErasureCheck.AsOrigin(
            "identity",
            ErasureRecipient.Fremde,
            [.. erasure.Adressen
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => entry.Key)]));
    }
}
