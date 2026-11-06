using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Noelia.Infrastructure.Security.M2M;

namespace WorkerTransfer.Tests.Shared;

/// <summary>
/// What a client of a service's real factory puts on the wire (ADR-0057).
/// </summary>
/// <remarks>
/// The stub factories of the wire tests skip the handler chain, so a forgotten
/// <c>AddServiceCall</c> is invisible to them; the client would get a 404 that
/// several services read as "nothing there". This goes through the chain of the
/// real <c>Program</c> and replaces only the two ends. Client names come from
/// the callers' constants: <c>CreateClient</c> accepts any name and answers an
/// unregistered one with a client without a token.
/// </remarks>
internal sealed class ServiceCallProbe
{
    private const string Prefix = "Bearer token-for-";

    private string? _authorization;

    private sealed class Source : IServiceTokenSource
    {
        public Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken = default) =>
            Task.FromResult("token-for-" + audience);
    }

    /// <summary>A transport the egress guard accepts, and that never connects.</summary>
    private sealed class Wire(ServiceCallProbe probe) : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            probe._authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>Replaces the token source and the end of every client's chain.</summary>
    public void Install(IServiceCollection services)
    {
        services.AddSingleton<IServiceTokenSource>(new Source());
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(
                builder => builder.PrimaryHandler = new Wire(this)));
    }

    /// <summary>The audience of the token a client sends; <c>null</c> without one.</summary>
    public async Task<string?> AudienceOf(IServiceProvider services, string client)
    {
        _authorization = null;

        using var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(client);
        using var response = await http.GetAsync(new Uri("http://identity-service:8001/probe"));

        return _authorization is { } value && value.StartsWith(Prefix, StringComparison.Ordinal)
            ? value[Prefix.Length..]
            : _authorization;
    }
}
