using Girder.Application.Extensions;
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
using WorkerTransfer.Profile.Infrastructure.Loeschung;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Infrastructure;

/// <summary>Beantwortet die Ports der Anwendungsschicht.</summary>
/// <remarks>
/// Alles, was dieser Dienst für sich entscheidet, steht hier in einem Aufruf —
/// und genauso laut, was <em>nicht</em> hier steht. Es gibt keinen Cache und
/// keinen zu registrieren: nichts implementiert Girders
/// <c>ICacheableQuery</c>, also lässt <c>AddCQRS</c> beide Cache-Behaviors ganz
/// aus der Pipeline. Das ist keine Sparsamkeit, das ist ADR-0013 — ein Widerruf
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

        services.Configure<Einwilligungseinstellungen>(
            configuration.GetSection(Einwilligungseinstellungen.Abschnitt));
        services.Configure<Entwurfseinstellungen>(
            configuration.GetSection(Entwurfseinstellungen.Abschnitt));
        services.Configure<Loescheinstellungen>(
            configuration.GetSection(Loescheinstellungen.Abschnitt));

        // Ein Browser hält den Token als httpOnly-Cookie und kann sonst nichts
        // damit anfangen.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.AuchAusDemCookie());

        services.AddSingleton(_ => ProfileDbContextFactory.Datenquelle(connectionString));
        services.AddDbContext<ProfileDbContext>((anbieter, optionen) =>
            ProfileDbContextFactory.Konfiguriere(
                optionen, anbieter.GetRequiredService<NpgsqlDataSource>()));

        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProfilspeicher, EfProfilspeicher>();
        services.AddScoped<IPruefspur, EfPruefspur>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();

        // Der Nullentwerfer ist die Voreinstellung, nicht der Rückfall: ohne
        // hinterlegten Schlüssel wird kein Fremddienst gerufen, und der Dienst
        // sagt das, statt eine Vorlage auszugeben, die wie ein Vorschlag
        // aussieht.
        var entwurf = new Entwurfseinstellungen();
        configuration.GetSection(Entwurfseinstellungen.Abschnitt).Bind(entwurf);

        if (string.IsNullOrEmpty(entwurf.Schluessel))
        {
            services.AddScoped<IEntwerfer, KeinEntwerfer>();
        }
        else
        {
            services.AddScoped<IEntwerfer, HttpEntwerfer>();
        }

        services.AddCQRS(typeof(ProfilSpeichernBefehl).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransaktionsBehavior<,>));

        return services;
    }
}
