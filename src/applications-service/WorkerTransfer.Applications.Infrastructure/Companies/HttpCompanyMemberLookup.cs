using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Infrastructure.Companies;

/// <summary>Where identity-service answers internal questions.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Identity__Adresse</c>, <c>Identity__Zeitueberschreitung</c>,
/// <c>Identity__Geheimnis</c>), so they stay German until the keys are renamed
/// everywhere at once — in compose, in the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class CompanyMemberSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds, like every other service-to-service call here. WITHOUT this
    /// line <c>HttpClient</c>'s default applies: a HUNDRED seconds.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The same secret as <c>Notifications:Geheimnis</c>.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Asks identity-service over the internal wire.</summary>
/// <remarks>
/// Calls <c>GET /internal/companies/{tenantId}/members</c>, with the same shared
/// secret header as the notification path. Reads the shared contract — never an
/// anonymous object, never a second type with the same fields.
/// </remarks>
public sealed class HttpCompanyMemberLookup(
    IHttpClientFactory factory,
    IOptions<CompanyMemberSettings> settings,
    ILogger<HttpCompanyMemberLookup> log) : ICompanyMemberLookup
{
    /// <summary>The name under which the client is registered.</summary>
    public const string ClientName = "identity-internal";

    private const string SecretHeader = "X-Notify-Secret";

    private readonly CompanyMemberSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubjectId>> GetAsync(
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            throw new CompanyLookupSilent("Identity:Adresse is not set.");
        }

        if (string.IsNullOrEmpty(_settings.Geheimnis))
        {
            throw new CompanyLookupSilent("Identity:Geheimnis is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_settings.Adresse), $"/internal/companies/{company.Value}/members"));
            request.Headers.Add(SecretHeader, _settings.Geheimnis);

            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning("identity-service (internal) unreachable ({Kind})", error.GetType().Name);
            throw new CompanyLookupSilent("identity-service (internal) is not reachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("identity-service (internal) did not answer in time");
            throw new CompanyLookupSilent("identity-service (internal) did not answer in time.", error);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CompanyLookupSilent(
                    "identity-service (internal) unreachable or secret invalid.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CompanyLookupSilent(
                    $"identity-service (internal) answered with {(int)response.StatusCode}.");
            }

            try
            {
                var read = await response.Content
                    .ReadFromJsonAsync<CompanyMembersResponseV1>(cancellationToken)
                    ?? throw new CompanyLookupSilent("identity-service (internal) sent nothing.");

                return [.. read.Mitglieder.Select(member => new SubjectId(member.SubjectId))];
            }
            catch (JsonException error)
            {
                throw new CompanyLookupSilent(
                    "identity-service (internal) sent an unusable response.", error);
            }
        }
    }
}
