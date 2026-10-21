using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    ILogger<HttpCompanyAiAccess> logger) : ICompanyAiAccess
{
    /// <summary>The name of the client in the factory.</summary>
    public const string ClientName = "identity-company-ai";

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

            // "none", or a row without key or model: the company writes with
            // the platform's model.
            if (body is null || !ModelWire.IsKnown(body.Provider ?? string.Empty)
                || string.IsNullOrEmpty(body.BaseUrl) || string.IsNullOrEmpty(body.Model)
                || string.IsNullOrEmpty(body.Key))
            {
                return null;
            }

            return body;
        }
    }
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

        return services;
    }
}
