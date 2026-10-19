using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Messages.Infrastructure.Security;

namespace WorkerTransfer.Messages.Infrastructure.Process;

/// <summary>Where the processes live.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Applications__BaseUrl</c>,
/// <c>Applications__Timeout</c>); renaming one alone leaves its value on the
/// default, silently.
/// </remarks>
public sealed class ApplicationsSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Applications";

    /// <summary>The base address of applications-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait — without it, <c>HttpClient</c>'s hundred seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The part of applications-service's answer this service reads — and only
/// that part.
/// </summary>
/// <remarks>
/// Its own type rather than a reference to <c>ApplicationV1</c>: the wire names
/// are the contract, and a test reads applications-service's real answer with
/// this type (the pattern of <c>NotificationWireTests</c>).
/// </remarks>
public sealed record ApplicationPartiesV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("applicant_contact")] ApplicantNameV1? ApplicantContact);

/// <summary>The name, and nothing else of the letterhead.</summary>
public sealed record ApplicantNameV1(
    [property: JsonPropertyName("full_name")] string? FullName);

/// <summary>Asks applications-service with the caller's own token.</summary>
/// <remarks>
/// <strong>The caller's token, not a service secret.</strong> The question is
/// "may THIS caller see the application", and only the caller's token can ask
/// it. A secret would answer "a service may" — true and useless.
/// </remarks>
public sealed class HttpProcessLookup(
    IHttpClientFactory factory,
    IOptions<ApplicationsSettings> settings,
    ICallerToken token,
    ILogger<HttpProcessLookup> log) : IProcessLookup
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "applications";

    private readonly ApplicationsSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<ProcessParties?> PartiesAsync(
        string kind, Guid reference, CancellationToken cancellationToken = default)
    {
        if (kind != ProcessKinds.Application)
        {
            return null;
        }

        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new ProcessLookupSilent("Applications:BaseUrl is not set.");
        }

        if (token.Value is not { Length: > 0 } bearer)
        {
            return null;
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.BaseUrl), $"/applications/{reference}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning("applications-service unreachable ({Kind})", error.GetType().Name);
            throw new ProcessLookupSilent("applications-service is not reachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProcessLookupSilent("applications-service did not answer in time.", error);
        }

        using (response)
        {
            // 404 and 401/403 are one answer: the caller does not see it.
            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProcessLookupSilent(
                    $"applications-service answered with {(int)response.StatusCode}.");
            }

            try
            {
                var body = await response.Content.ReadFromJsonAsync<ApplicationPartiesV1>(cancellationToken)
                    ?? throw new ProcessLookupSilent("applications-service sent nothing.");

                return new ProcessParties(
                    new SubjectId(body.SubjectId),
                    new TenantId(body.TenantId),
                    body.ApplicantContact?.FullName ?? string.Empty);
            }
            catch (JsonException error)
            {
                throw new ProcessLookupSilent("applications-service sent an unusable answer.", error);
            }
        }
    }
}
