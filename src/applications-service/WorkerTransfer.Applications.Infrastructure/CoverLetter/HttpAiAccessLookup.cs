using System.Net;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.Companies;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Infrastructure.CoverLetter;

/// <summary>Fetches the person's AI access from identity-service.</summary>
/// <remarks>
/// The same internal wire and the same secret as the member lookup. The key
/// travels only here, never through the gateway, never into a log line.
/// <para>
/// <strong>localhost from inside the container is the wrong machine.</strong>
/// Ollama and MiniMax run on the person's computer. In the compose stack that is
/// <c>host.docker.internal</c> (extra_hosts). The interface may go on showing
/// <c>http://localhost:11434/…</c> — it is rewritten only here, and only inside
/// the container.
/// </para>
/// </remarks>
public sealed class HttpAiAccessLookup(
    IHttpClientFactory factory,
    IOptions<CompanyMemberSettings> identity,
    IOptions<CoverLetterSettings> draft,
    ILogger<HttpAiAccessLookup> log) : IAiAccessLookup
{
    /// <summary>The name under which the client is registered.</summary>
    public const string ClientName = "identity-internal";

    private const string SecretHeader = "X-Notify-Secret";

    private readonly CompanyMemberSettings _identity = identity.Value;
    private readonly CoverLetterSettings _draft = draft.Value;

    /// <inheritdoc />
    public async Task<AiAccess> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var fromAccount = await FromAccountAsync(subject, cancellationToken);

        if (fromAccount.IsSetUp)
        {
            return fromAccount;
        }

        if (_draft.Schluessel.Length > 0)
        {
            return new AiAccess(
                "anthropic",
                _draft.Adresse,
                _draft.Modell,
                _draft.Schluessel);
        }

        return fromAccount;
    }

    private async Task<AiAccess> FromAccountAsync(
        SubjectId subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_identity.Adresse) || string.IsNullOrEmpty(_identity.Geheimnis))
        {
            return new AiAccess("none", string.Empty, string.Empty, string.Empty);
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _identity.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_identity.Adresse), $"/internal/account/{subject.Value}/ai"));
            request.Headers.Add(SecretHeader, _identity.Geheimnis);

            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning(
                "identity-service (AI access) unreachable ({Kind})", error.GetType().Name);
            throw new CoverLetterUnavailable(
                "identity-service (internal) is not reachable.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CoverLetterUnavailable(
                "identity-service (internal) does not answer (timeout).");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new AiAccess("none", string.Empty, string.Empty, string.Empty);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CoverLetterUnavailable(
                    $"identity-service (internal) answers with {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<KiZugangV1>(cancellationToken)
                ?? throw new CoverLetterUnavailable(
                    "identity-service (internal) sent an unusable or empty response.");

            return new AiAccess(
                body.Provider ?? "none",
                ForTheContainer(Complete(body.BaseUrl ?? string.Empty, body.Provider)),
                body.Model ?? string.Empty,
                body.Key ?? string.Empty);
        }
    }

    /// <summary>
    /// A bare Ollama address without a path gets the OpenAI path.
    /// </summary>
    public static string Complete(string address, string? provider)
    {
        var trimmed = address.Trim().TrimEnd('/');

        if (provider != "openai_compatible" || trimmed.Length == 0)
        {
            return trimmed;
        }

        if (trimmed.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/v1/chat/completions";
    }

    /// <summary>
    /// Inside the container localhost is the container. Ollama runs on the machine.
    /// </summary>
    public static string ForTheContainer(string address)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return address;
        }

        return address
            .Replace("://localhost", "://host.docker.internal", StringComparison.OrdinalIgnoreCase)
            .Replace("://127.0.0.1", "://host.docker.internal", StringComparison.Ordinal);
    }
}
