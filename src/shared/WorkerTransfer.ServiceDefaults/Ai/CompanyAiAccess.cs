using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.ServiceDefaults.Ai;

/// <summary>Which model writes for a company — its own key, or none (ADR-0051, Bauliste 6).</summary>
/// <remarks>
/// A port, so the suites answer it out of a fake instead of starting
/// identity-service beside every company seam.
/// </remarks>
public interface ICompanyAiAccess
{
    /// <summary>The company's own access, or <c>null</c> when it set none up.</summary>
    /// <param name="company">The company the caller acts for — from the verified token.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="CompanyAiAccessSilent">
    /// identity-service did not answer. <strong>Not the same as <c>null</c>:</strong>
    /// falling back to the platform's model then would send the company's text
    /// to a provider it may have chosen against.
    /// </exception>
    Task<AiAccessV1?> AccessAsync(Guid company, CancellationToken cancellationToken = default);
}

/// <summary>The company's AI access could not be had — the seam answers 503, never writes.</summary>
public sealed class CompanyAiAccessSilent : Exception
{
    /// <summary>With a reason that names no values.</summary>
    /// <param name="message">What was missing — a shape, never a value.</param>
    public CompanyAiAccessSilent(string message) : base(message)
    {
    }

    /// <summary>With a reason and the cause underneath it.</summary>
    /// <param name="message">What was missing.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public CompanyAiAccessSilent(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Parameterless, because the analyser demands it.</summary>
    public CompanyAiAccessSilent()
    {
    }
}

/// <summary>Asks identity-service per draft, with no store.</summary>
/// <remarks>
/// <para><strong>No cache:</strong> an admin who removes the key must stop the
/// next draft from using it, not the one after a lifetime.</para>
///
/// <para><strong>Unconfigured means "none set up"</strong>, as on the person's
/// side: without <c>Identity__BaseUrl</c> there is nobody to ask, and the seam
/// writes with the platform's model or not at all. <strong>A 404 is NOT
/// "none":</strong> the door answers every company with 200 and refuses only a
/// wrong secret with 404 — read as "none", a mis-wired secret would send every
/// company's drafts to the platform's provider, and nobody would notice.</para>
/// </remarks>
public sealed class HttpCompanyAiAccess(
    IHttpClientFactory clientFactory,
    IOptions<CompanyRoleSettings> settings,
    CompanyAiHosts hosts,
    ILogger<HttpCompanyAiAccess> logger) : ICompanyAiAccess
{
    /// <summary>The name of the client in the factory.</summary>
    public const string ClientName = "identity-company-ai";

    /// <summary>
    /// The client a seam asks a company's model with: public addresses only,
    /// no redirects. A company's own server lies under its domain, and that
    /// domain must not point inside.
    /// </summary>
    public const string ModelClientName = "company-model";

    private const string SecretHeader = "X-Notify-Secret";

    private readonly CompanyRoleSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<AiAccessV1?> AccessAsync(Guid company, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl) || string.IsNullOrEmpty(_settings.Secret))
        {
            return null;
        }

        using var client = clientFactory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_settings.BaseUrl), $"/internal/companies/{company}/ai"));
            request.Headers.Add(SecretHeader, _settings.Secret);

            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning("Company AI access unreachable ({Kind})", error.GetType().Name);
            throw new CompanyAiAccessSilent("identity-service (internal) is unreachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Company AI access did not answer in time");
            throw new CompanyAiAccessSilent("identity-service (internal) did not answer in time.", error);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CompanyAiAccessSilent("identity-service (internal) refused the secret.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CompanyAiAccessSilent(
                    $"identity-service (internal) answered with {(int)response.StatusCode}.");
            }

            AiAccessV1? body;

            try
            {
                body = await response.Content.ReadFromJsonAsync<AiAccessV1>(cancellationToken);
            }
            catch (JsonException error)
            {
                throw new CompanyAiAccessSilent("identity-service (internal) sent an unusable answer.", error);
            }

            // "none", or a row without model: the company writes with the
            // platform's model. A cloud provider needs a key; the company's own
            // server may need none.
            if (body is null || !ModelWire.IsKnown(body.Provider ?? string.Empty)
                || !Uri.TryCreate(body.BaseUrl, UriKind.Absolute, out var address)
                || address.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrEmpty(body.Model)
                || (body.Provider == ModelWire.Anthropic && string.IsNullOrEmpty(body.Key)))
            {
                return null;
            }

            // identity-service checked the host against the company's proven
            // domain when it was saved; the egress boundary learns it here,
            // just before the one call that needs it.
            hosts.Remember(address.Host);

            return body with { Key = body.Key ?? string.Empty };
        }
    }
}

/// <summary>The hosts of companies' own model servers this process has been handed.</summary>
/// <remarks>
/// Learned from identity-service's answer, never from a request: identity
/// stores an own server only under the company's proven domain, https, no IP
/// address. A host stays known until the process ends — it was a company's
/// proven domain, and the connect step still refuses anything inside.
/// </remarks>
public sealed class CompanyAiHosts
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _hosts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Learns a host.</summary>
    /// <param name="host">The host identity-service handed out.</param>
    public void Remember(string host)
    {
        if (!string.IsNullOrWhiteSpace(host))
        {
            _hosts.TryAdd(host.Trim().TrimEnd('.'), 0);
        }
    }

    /// <summary>Whether the host was handed out.</summary>
    /// <param name="host">The host of a destination.</param>
    public bool Knows(string host) => _hosts.ContainsKey(host);

    /// <summary>The hosts, for the sovereignty report.</summary>
    public IReadOnlyCollection<string> All => [.. _hosts.Keys.Order(StringComparer.Ordinal)];
}

/// <summary>Noelia's egress policy, widened by the companies' own model servers.</summary>
/// <remarks>
/// <strong>Through Noelia's interface, not around it.</strong> The policy is
/// fixed at startup, a company's server comes later; every other decision stays
/// the inner policy's, and the hosts stand in the report. Only https.
/// </remarks>
/// <param name="inner">The policy this one widens.</param>
/// <param name="hosts">What identity-service handed out.</param>
public sealed class CompanyAiEgress(IEgressPolicy inner, CompanyAiHosts hosts) : IEgressPolicy
{
    /// <summary>The policy this one widens — for the suites that check the chain.</summary>
    public IEgressPolicy Inner => inner;

    /// <inheritdoc />
    public bool IsAllowed(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return inner.IsAllowed(destination)
               || (destination.Scheme == Uri.UriSchemeHttps && hosts.Knows(destination.Host));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DeclaredHosts => [.. inner.DeclaredHosts, .. hosts.All];

    /// <inheritdoc />
    public bool IsEnforcing => inner.IsEnforcing;
}

/// <summary>Registers the company AI lookup.</summary>
public static class CompanyAiAccessExtensions
{
    /// <summary>Wires <see cref="ICompanyAiAccess"/> to identity-service's internal door.</summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where <c>Identity:BaseUrl</c> and <c>Identity:Secret</c> come from.</param>
    /// <returns>The container.</returns>
    public static IServiceCollection AddCompanyAiAccess(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<CompanyRoleSettings>(configuration.GetSection(CompanyRoleSettings.Section));
        services.AddHttpClient(HttpCompanyAiAccess.ClientName);
        services.TryAddScoped<ICompanyAiAccess, HttpCompanyAiAccess>();

        services.AddHttpClient(HttpCompanyAiAccess.ModelClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectCallback = PublicAddress.ConnectAsync,
                AllowAutoRedirect = false
            });

        var hosts = new CompanyAiHosts();
        services.AddSingleton(hosts);

        // Noelia's egress policy, widened through its interface. Registered by
        // `AddWorkerTransferDefaults` before this line — and possibly widened
        // already (jobs-service's company feeds); this wraps whatever stands.
        var policy = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IEgressPolicy));

        if (policy?.ImplementationInstance is IEgressPolicy inner)
        {
            services.Remove(policy);
            services.AddSingleton<IEgressPolicy>(new CompanyAiEgress(inner, hosts));
        }

        return services;
    }
}
