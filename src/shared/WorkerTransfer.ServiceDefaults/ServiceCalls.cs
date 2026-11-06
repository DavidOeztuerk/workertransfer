using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Noelia.Infrastructure.Security.M2M;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>A client that goes through another service's door (ADR-0057).</summary>
public static class ServiceCalls
{
    /// <summary>Makes every request of this client carry a service token.</summary>
    /// <remarks>
    /// The only way a client here gets a token, and only ever onto a client of
    /// its own: a token on a client that also calls foreign addresses would
    /// hand it to them.
    /// </remarks>
    /// <param name="builder">The client's builder.</param>
    /// <param name="audience">The service called, from <see cref="ServiceAudiences"/>.</param>
    public static IHttpClientBuilder AddServiceCall(this IHttpClientBuilder builder, string audience)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Transient<IStartupFilter, TokenSourceGuard>());

        // Outermost first: the translation has to stand around the token handler.
        return builder
            .AddHttpMessageHandler(() => new SilentWithoutToken())
            .AddServiceToken(audience);
    }

    /// <summary>Turns "no token" into what every door caller already handles.</summary>
    /// <remarks>
    /// Callers catch <see cref="HttpRequestException"/> and go silent, and the
    /// promises hang on that: a silent role lookup answers 503, never 403.
    /// <see cref="ServiceTokenUnavailableException"/> is none, so it would be a
    /// 500. A cancellation passes untouched: the callers tell a time limit from
    /// their own abort, and this must not take that from them.
    /// </remarks>
    private sealed class SilentWithoutToken : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (ServiceTokenUnavailableException error)
            {
                throw new HttpRequestException("No service token could be had.", error);
            }
        }
    }

    /// <summary>Refuses to start a service that could not fetch a token.</summary>
    /// <remarks>
    /// Without the module the factory throws inside <c>CreateClient</c>, which
    /// the callers call outside their <c>try</c>: a 500 at the first request,
    /// instead of this at startup.
    /// </remarks>
    private sealed class TokenSourceGuard(IServiceProviderIsService container) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            if (!container.IsService(typeof(IServiceTokenSource)))
            {
                throw new InvalidOperationException(
                    "A client uses AddServiceCall, but this service does not compose "
                    + "UseServiceTokenClient(): it could not fetch a token.");
            }

            return next;
        }
    }
}
