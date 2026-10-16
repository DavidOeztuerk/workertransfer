using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Identity;

/// <summary>identity-service's internal doors, and the paper that opens them.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Identity__BaseUrl</c>,
/// <c>Identity__Secret</c>, <c>Identity__Timeout</c>) — the same two the role
/// lookup reads; renaming one alone leaves its value on the default, silently.
/// </remarks>
public sealed class IdentitySettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The services' shared secret. Empty shuts the door.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>How long to wait.</summary>
    /// <remarks>Without it <c>HttpClient</c> waits a hundred seconds — a hanging call, not a timeout.</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks identity-service — with the service secret, never with a company's token.</summary>
public sealed class HttpIdentityLookup(
    IHttpClientFactory factory,
    IOptions<IdentitySettings> settings,
    ILogger<HttpIdentityLookup> log) : IIdentityLookup
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "identity-internal";

    private const string SecretHeader = "X-Notify-Secret";

    private readonly IdentitySettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> CompanyExistsAsync(TenantId company, CancellationToken cancellationToken = default) =>
        await ReadAsync<CompanyLookupV1>($"/internal/companies/{company.Value}", cancellationToken) is not null;

    /// <inheritdoc />
    public async Task<string?> PersonNameAsync(SubjectId subject, CancellationToken cancellationToken = default) =>
        (await ReadAsync<PersonLookupV1>($"/internal/account/{subject.Value}/identity", cancellationToken))?.Name;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubjectId>> MembersAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var read = await ReadAsync<CompanyMembersResponseV1>(
            $"/internal/companies/{company.Value}/members", cancellationToken);

        return read is null ? [] : [.. read.Members.Select(member => new SubjectId(member.SubjectId))];
    }

    /// <summary>A GET behind the secret. <c>null</c> means "does not exist".</summary>
    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl) || string.IsNullOrEmpty(_settings.Secret))
        {
            // Not configured is a SYSTEM state, never "this company does not
            // exist" — that would turn a missing line in compose into a 404.
            throw new IdentitySilent("Identity:BaseUrl or Identity:Secret is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_settings.BaseUrl), path));
        request.Headers.Add(SecretHeader, _settings.Secret);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning("identity-service (internal) unreachable ({Kind})", error.GetType().Name);
            throw new IdentitySilent("identity-service (internal) is not reachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IdentitySilent("identity-service (internal) did not answer in time.", error);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IdentitySilent($"identity-service (internal) answered with {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                       ?? throw new IdentitySilent("identity-service (internal) sent nothing.");
            }
            catch (JsonException error)
            {
                throw new IdentitySilent("identity-service (internal) sent an unusable answer.", error);
            }
        }
    }
}
