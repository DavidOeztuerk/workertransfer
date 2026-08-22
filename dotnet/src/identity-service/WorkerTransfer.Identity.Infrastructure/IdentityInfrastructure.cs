using Girder.Application.Extensions;
using Girder.Data.EntityFrameworkCore.Sessions;
using Girder.Infrastructure.Security.Identity;
using Girder.Passwords.BCrypt;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Application.Behaviors;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Security;

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
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

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
        services.AddScoped(
            typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
