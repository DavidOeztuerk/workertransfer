using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Messages.Application.Ports;

namespace WorkerTransfer.Messages.Infrastructure.Companies;

/// <summary>identity-service's internal door, and the paper that opens it.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Identity__BaseUrl</c>,
/// <c>Identity__Timeout</c>, <c>Identity__Secret</c>); renaming one alone
/// leaves its value on the default, silently.
/// </remarks>
public sealed class IdentitySettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The services' shared secret. Empty shuts the door.</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>A company's members, from identity-service.</summary>
public sealed class HttpCompanyMemberLookup(
    IHttpClientFactory factory,
    IOptions<IdentitySettings> settings,
    ILogger<HttpCompanyMemberLookup> log) : ICompanyMemberLookup
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "identity-internal";

    private readonly IdentitySettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubjectId>> MembersAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl) || string.IsNullOrEmpty(_settings.Secret))
        {
            throw new ProcessLookupSilent("Identity:BaseUrl or Identity:Secret is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(_settings.BaseUrl), $"/internal/companies/{company.Value}/members"));
        request.Headers.Add("X-Notify-Secret", _settings.Secret);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning("identity-service (internal) unreachable ({Kind})", error.GetType().Name);
            throw new ProcessLookupSilent("identity-service (internal) is not reachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProcessLookupSilent("identity-service (internal) did not answer in time.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ProcessLookupSilent(
                    $"identity-service (internal) answered with {(int)response.StatusCode}.");
            }

            try
            {
                var read = await response.Content.ReadFromJsonAsync<CompanyMembersResponseV1>(cancellationToken)
                    ?? throw new ProcessLookupSilent("identity-service (internal) sent nothing.");

                return [.. read.Members.Select(member => new SubjectId(member.SubjectId))];
            }
            catch (JsonException error)
            {
                throw new ProcessLookupSilent("identity-service (internal) sent an unusable answer.", error);
            }
        }
    }
}
