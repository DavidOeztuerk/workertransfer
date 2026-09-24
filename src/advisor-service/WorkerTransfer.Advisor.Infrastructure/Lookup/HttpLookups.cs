using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;

namespace WorkerTransfer.Advisor.Infrastructure.Lookup;

/// <summary>Where identity-service has its internal doors.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Identity__Adresse</c>,
/// <c>Identity__Geheimnis</c>, <c>Identity__Geduld</c>), so they stay German
/// until the keys are renamed everywhere at once — in compose, in the chart and
/// in <c>.env.example</c>.
/// </remarks>
public sealed class LookupSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    /// <remarks>
    /// It stands in the configuration and not in the source, because the egress
    /// boundary derives its allowed hosts from there — a destination that stood
    /// only here in the code would be refused, and without a log line.
    /// </remarks>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>The shared secret of the internal doors.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Without this line <c>HttpClient</c>'s default applies: a hundred
    /// seconds. That is not a timeout but a hanging call.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>What the internal door says about a company.</summary>
internal sealed record CompanyResponseV1(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("domain")] string? Domain);

/// <summary>What it says about a human being.</summary>
internal sealed record PersonResponseV1(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email);

/// <summary>Asks identity-service — behind the shared secret.</summary>
/// <remarks>
/// <para><strong>Two doors, two very different questions.</strong> The company
/// lookup names a domain, so that an exclusion can take effect at all; the
/// person lookup names a civil name, and it is called only <em>after</em> the
/// ledger has confirmed stage 3.</para>
///
/// <para>With the service secret and not with the caller's token: the caller
/// here is the <em>company</em>, and it must not be able to fetch a human
/// being's civil name on its own authority. What it may do is decided one line
/// higher by the ledger — and that is why this door is so narrow: two fields,
/// no postal address, no phone.</para>
/// </remarks>
public sealed class HttpLookups(
    IHttpClientFactory factory,
    IOptions<LookupSettings> settings,
    ILogger<HttpLookups> logger) : ICompanyLookup, IPersonLookup
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "identity";

    private const string SecretHeader = "X-Notify-Secret";

    private readonly LookupSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<CompanyCard?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var response = await ReadAsync<CompanyResponseV1>(
            $"/internal/companies/{company.Value}", cancellationToken);

        return response is null ? null : new CompanyCard(response.Name ?? string.Empty,
            response.Domain ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<PersonCard?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var response = await ReadAsync<PersonResponseV1>(
            $"/internal/account/{subject.Value}/identity", cancellationToken);

        return response is null ? null : new PersonCard(response.Name ?? string.Empty,
            response.Email ?? string.Empty);
    }

    /// <summary>A GET behind the secret. <c>null</c> means "does not exist".</summary>
    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_settings.Adresse)
            || string.IsNullOrEmpty(_settings.Geheimnis))
        {
            // Not configured is a SYSTEM state and not a statement about a
            // human being. Answering `null` quietly would mean "this company
            // does not exist" here — and an exclusion would then never apply.
            throw new LookupSilent("Identity:Adresse or :Geheimnis is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.Adresse), path));

        request.Headers.Add(SecretHeader, _settings.Geheimnis);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning(
                "identity-service is unreachable: {Kind}", error.GetType().Name);

            throw new LookupSilent("identity-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("identity-service did not answer in time.");

            throw new LookupSilent("identity-service timed out");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // The door answers 404 even when the secret does not match —
                // deliberately, so that it does not give itself away through a
                // method. Here it therefore only means: no information.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "identity-service answered with {Status}.", (int)response.StatusCode);

                throw new LookupSilent(
                    $"identity-service returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (Exception error) when (error is not LookupSilent)
            {
                logger.LogWarning("identity-service sent an unreadable answer.");

                throw new LookupSilent("identity-service sent an unusable answer");
            }
        }
    }
}
