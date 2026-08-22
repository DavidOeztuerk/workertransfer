using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// The one call every service makes, and the one that decides nothing a service
/// has to decide for itself.
/// </summary>
/// <remarks>
/// What is here is here because ten services answering it ten ways would be ten
/// chances to answer it wrong: how a token is verified, what a failure looks
/// like on the wire, in which order the pipeline runs. What is <em>not</em>
/// here is everything that is a decision — which database, which repositories,
/// whether there is a cache, whether there is an outbox. Those live in each
/// service's own <c>Add&lt;Dienst&gt;Infrastructure()</c>, where a reader can
/// see them.
/// </remarks>
public static class Dienstgrundlage
{
    /// <summary>
    /// Verification, context, headers, health, telemetry — and no issuing.
    /// </summary>
    /// <remarks>
    /// <c>AddJwtAuthentication()</c> gives a service the ability to
    /// <em>verify</em> a token from its signature. Issuing one needs the private
    /// half of the key, and only identity-service has it — see
    /// <see cref="AlsAussteller"/>. That is not a convention: a second issuer is
    /// a second place that can mint a principal, and nothing downstream could
    /// tell the two apart.
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="dienstname">What it calls itself in logs and telemetry.</param>
    /// <param name="weitere">Modules only this service needs.</param>
    public static IServiceCollection AddWorkerTransferDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string dienstname,
        Action<InfrastructureBuilder>? weitere = null) =>
        services.AddSharedInfrastructure(
            configuration, environment, dienstname, infrastruktur =>
            {
                infrastruktur
                    .AddJwtAuthentication()
                    .AddPrincipal()
                    .AddSecurityHeaders()
                    .AddHealthChecks()
                    .AddObservability();

                weitere?.Invoke(infrastruktur);
            });

    /// <summary>
    /// The extra a service needs to <em>issue</em> tokens. identity-service only.
    /// </summary>
    /// <remarks>
    /// Its own method with its own name so that a search for it finds exactly
    /// one composition root. A reviewer should be able to answer "who can mint a
    /// token here?" by grepping, not by reading ten files.
    /// </remarks>
    public static InfrastructureBuilder AlsAussteller(this InfrastructureBuilder infrastruktur)
    {
        ArgumentNullException.ThrowIfNull(infrastruktur);

        return infrastruktur.AddPasswordHashing().AddTokenSessions();
    }

    /// <summary>The pipeline, in the order that makes each step mean something.</summary>
    /// <remarks>
    /// Correlation first, so everything after it can be filed under one request.
    /// Security headers before anything that writes a body. Authentication
    /// before the principal, because the principal is built from the verified
    /// token. The problem-details middleware last of the cross-cutting ones and
    /// ahead of the endpoints, so it wraps handlers and not the health probes —
    /// a probe that answers a problem document is a probe that fails a check for
    /// the wrong reason.
    /// </remarks>
    /// <param name="app">The application being built.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="dienstname">What it calls itself.</param>
    public static WebApplication UseWorkerTransferDefaults(
        this WebApplication app,
        IHostEnvironment environment,
        string dienstname)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSharedInfrastructure(environment, dienstname, pipeline => pipeline
            .UseCorrelationId()
            .UseSecurityHeaders()
            .UseAuth()
            .UsePrincipal()
            .UseHealthCheckEndpoints());

        app.UseMiddleware<ProblemDetailsMiddleware>();

        return app;
    }
}
