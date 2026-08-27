using Girder.Application.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorkerTransfer.Consent.Application.Behaviors;
using WorkerTransfer.Consent.Application.Einwilligung;
using WorkerTransfer.Consent.Application.Ports;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;
using WorkerTransfer.Consent.Infrastructure.Loeschung;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.Consent.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Infrastructure;

/// <summary>Answers the application's ports.</summary>
/// <remarks>
/// Everything this service decides for itself stands here, in one call: which
/// database, which repositories, which behaviours — and, just as loudly, what
/// is <em>not</em> here. There is no cache of any kind, and there is no
/// distributed cache to register: nothing in this service implements Girder's
/// <c>ICacheableQuery</c>, so <c>AddCQRS</c> leaves both cache behaviours out
/// of the pipeline entirely. That is not thrift, it is ADR-0013 — a withdrawal
/// has to take effect on the very next read, and a cache here is not a
/// performance detail but a broken promise.
/// </remarks>
public static class ConsentInfrastructure
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where the erasure secret comes from.</param>
    /// <param name="connectionString">Where the consent database lives.</param>
    public static IServiceCollection AddConsentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        // A browser holds the token as an httpOnly cookie and can do nothing
        // else with it. Without this every switch on the consent page comes
        // back 401 while the sign-in looks like it worked.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.AuchAusDemCookie());

        services.AddSingleton(_ => ConsentDbContextFactory.DataSource(connectionString));
        services.AddDbContext<ConsentDbContext>((provider, options) =>
            ConsentDbContextFactory.Konfiguriere(
                options, provider.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IConsentLedger, EfConsentLedger>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IFreitextraeumung, EfFreitextraeumung>();
        services.AddScoped<Einwilligungsschreiber>();
        services.AddSingleton<IKorrelation, HttpKorrelation>();

        // Seven handlers, so the mediator earns its keep. Registered before the
        // transaction behaviour so that one ends up innermost: Girder's logging
        // and validation run before anything is written, which is where a
        // rejected command costs nothing.
        services.AddCQRS(typeof(EinwilligungErteilenBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
