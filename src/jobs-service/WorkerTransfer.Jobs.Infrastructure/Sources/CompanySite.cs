using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Jobs.Application.Ports;

namespace WorkerTransfer.Jobs.Infrastructure.Sources;

/// <summary>
/// The domains with a source — what the egress boundary lets through besides
/// the configuration (ADR-0052).
/// </summary>
public sealed class KnownDomains : IKnownDomains
{
    private readonly ConcurrentDictionary<string, byte> _domains = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Remember(string domain)
    {
        if (!string.IsNullOrWhiteSpace(domain))
        {
            _domains.TryAdd(domain.Trim().TrimEnd('.'), 0);
        }
    }

    /// <summary>Whether a host is one of the domains or lies below one.</summary>
    public bool Covers(string host) =>
        _domains.Keys.Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                                    || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    /// <summary>The domains, for the sovereignty report.</summary>
    public IReadOnlyCollection<string> All => [.. _domains.Keys.Order(StringComparer.Ordinal)];
}

/// <summary>
/// Noelia's egress policy, widened by exactly the companies' proven domains
/// with a source (ADR-0052).
/// </summary>
/// <remarks>
/// <para><strong>Through Noelia's interface, not around it.</strong> The
/// policy is fixed at startup; a company's domain comes later. Every other
/// decision stays Noelia's, and the domains stand in the report.</para>
///
/// <para>Only https, and only a host on a proven domain — a company's feed,
/// nothing else of the internet. Besides those: the hosts below
/// Personio's domain (<c>Sources__Personio</c>), because every Personio
/// company has a host of its own there.</para>
/// </remarks>
public sealed class CompanyDomainEgress(IEgressPolicy inner, KnownDomains known, string personio) : IEgressPolicy
{
    private readonly string _personio = personio.Trim().Trim('.');

    /// <inheritdoc />
    public bool IsAllowed(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return inner.IsAllowed(destination)
               || (destination.Scheme == Uri.UriSchemeHttps
                   && (known.Covers(destination.Host) || BelowPersonio(destination.Host)));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DeclaredHosts =>
        [.. inner.DeclaredHosts, .. _personio.Length > 0 ? [$"*.{_personio}"] : Array.Empty<string>(), .. known.All];

    private bool BelowPersonio(string host) =>
        _personio.Length > 0 && host.EndsWith("." + _personio, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsEnforcing => inner.IsEnforcing;
}

/// <summary>Reads a company's own feed, on its proven domain.</summary>
/// <remarks>
/// <para><strong>A company's domain must not point inside.</strong> Whoever
/// resolves their domain to <c>10.0.0.5</c> would otherwise reach the services
/// in the cluster through this read. The client's connect step checks the
/// resolved address and refuses loopback, private, link-local and carrier-grade
/// ranges — at connect time, so a second resolution cannot slip past.</para>
///
/// <para>Redirects are followed by hand, at most three, and only while they
/// stay on the same host or below the first one's domain: the egress boundary
/// switched automatic ones off, and a redirect off the company's domain would
/// be a fetch nobody bound.</para>
/// </remarks>
public sealed class HttpCompanySite(
    IHttpClientFactory factory,
    IOptions<SourceSettings> settings,
    ILogger<HttpCompanySite> log) : ICompanySite
{
    /// <summary>The client with the public-address check.</summary>
    public const string ClientName = "company-site";

    private readonly SourceSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string?> PageAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var current) || current.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var origin = current.Host;
        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.PageTimeout;

        for (var hop = 0; hop <= 3; hop++)
        {
            HttpResponseMessage response;

            try
            {
                response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception error) when (error is HttpRequestException or EgressDeniedException
                                              || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                log.LogWarning("A company page could not be read ({Kind})", error.GetType().Name);
                return null;
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
                {
                    var target = next.IsAbsoluteUri ? next : new Uri(current, next);

                    if (target.Scheme != Uri.UriSchemeHttps
                        || !(target.Host.Equals(origin, StringComparison.OrdinalIgnoreCase)
                             || target.Host.EndsWith("." + Parent(origin), StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }

                    current = target;
                    continue;
                }

                return response.IsSuccessStatusCode
                    ? await Limited.ReadAsync(response, _settings.PageMaxBytes, cancellationToken)
                    : null;
            }
        }

        return null;
    }

    private static string Parent(string host)
    {
        var dot = host.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && host.IndexOf('.', dot + 1) > 0 ? host[(dot + 1)..] : host;
    }
}

/// <summary>identity-service's internal door, for the company's proven domain.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Identity__BaseUrl</c>,
/// <c>Identity__Secret</c>, <c>Identity__Timeout</c>) — the same two the role
/// lookup reads.
/// </remarks>
public sealed class IdentityLookupSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The services' shared secret.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>How long to wait.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>A company's proven domain (ADR-0019), behind the shared secret.</summary>
public sealed class HttpCompanyDomains(
    IHttpClientFactory factory,
    IOptions<IdentityLookupSettings> settings) : ICompanyDomains
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "identity-internal";

    private readonly IdentityLookupSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string?> DomainAsync(TenantId company, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl) || string.IsNullOrEmpty(_settings.Secret))
        {
            throw new SourceSilent("Identity:BaseUrl or Identity:Secret is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.BaseUrl), $"/internal/companies/{company.Value}"));
        request.Headers.Add("X-Notify-Secret", _settings.Secret);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException
                                          || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SourceSilent("identity-service (internal) is not reachable.", error);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SourceSilent($"identity-service (internal) answered {(int)response.StatusCode}.");
            }

            return (await response.Content.ReadFromJsonAsync<CompanyLookupV1>(cancellationToken))?.Domain;
        }
    }
}
