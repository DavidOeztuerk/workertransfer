using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.Jobs.Infrastructure.Draft;

/// <summary>Whether and where a model is asked at all.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Draft__ApiKey</c>,
/// <c>Draft__BaseUrl</c>, <c>Draft__Timeout</c>,
/// <c>Draft__Model</c>, <c>Draft__Provider</c>); renaming one alone leaves its value on the default,
/// silently.
/// </remarks>
public sealed class DraftSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Draft";

    /// <summary>
    /// Empty means: no foreign service is called, and the interface says so.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The provider's address.</summary>
    public string BaseUrl { get; set; } = ModelWire.DefaultAddress;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Thirty and not five seconds: at the other end stands a language model,
    /// and a draft takes longer than a query. But not unlimited either —
    /// without this line <c>HttpClient</c>'s default applies with a HUNDRED
    /// seconds, and nobody is to sit in front of a form that long.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Which model.</summary>
    public string Model { get; set; } = ModelWire.DefaultModel;

    /// <summary>
    /// Which wire format the address speaks: <c>openai_compatible</c>
    /// (Ollama until the start, ADR-0051) or <c>anthropic</c>.
    /// </summary>
    public string Provider { get; set; } = ModelWire.OpenAiCompatible;
}

/// <summary>Asks a model, once, and keeps nothing.</summary>
/// <remarks>
/// <strong>Which model:</strong> the company's own key if an admin stored one,
/// otherwise the platform's (ADR-0051). A silent identity-service is refused,
/// never replaced by the platform's provider.
/// </remarks>
public sealed class HttpDrafter(
    IHttpClientFactory factory,
    IOptions<DraftSettings> settings,
    ICompanyAiAccess companyAi,
    ILogger<HttpDrafter>? log = null) : IDrafter
{
    private readonly DraftSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string> DraftAsync(
        AdvertDraft draft, TenantId company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        AiAccessV1? own;

        try
        {
            own = await companyAi.AccessAsync(company.Value, cancellationToken);
        }
        catch (CompanyAiAccessSilent)
        {
            throw new DraftUnavailable("identity-service (internal) is not reachable.");
        }

        var access = own ?? (string.IsNullOrEmpty(_settings.ApiKey)
            ? null
            : new AiAccessV1(_settings.Provider, _settings.BaseUrl, _settings.Model, _settings.ApiKey));

        if (access is null)
        {
            // web/src/features/company/pages/CompanyJobNewPage.tsx (HELP_CASES) matches this sentence.
            throw new DraftUnavailable("No draft provider is set up.");
        }

        // A company's own access goes through the client that reaches public
        // addresses only — its server lies under its domain, and that domain
        // must not point inside.
        using var client = factory.CreateClient(own is null ? nameof(HttpDrafter) : HttpCompanyAiAccess.ModelClientName);

        client.Timeout = _settings.Timeout;

        // What goes out stands in AdvertDraft and nowhere else: no
        // tenant_id, no company name, nothing about a human being.
        using var request = ModelWire.Request(
            access.Provider, access.BaseUrl, access.Model, access.Key,
            AdvertDraft.Rules, draft.Prompt, maxTokens: 800);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // The kind, never the content.
            throw new DraftUnavailable(
                $"The draft provider does not answer ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // An expired `client.Timeout` comes as TaskCanceledException and NOT
            // as HttpRequestException — without this branch it would leave the
            // service as a 500, although it is the same statement as a
            // connection error: the provider does not answer.
            //
            // The condition separates the two cases that share the same
            // exception type. If the caller's token is cancelled, the browser
            // hung up — then nothing at all is broken, and the exception belongs
            // passed on instead of turned into a 503.
            throw new DraftUnavailable(
                "The draft provider does not answer (timeout).");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new DraftUnavailable(
                    $"The draft provider answers with {(int)response.StatusCode}.");
            }

            string? text;

            try
            {
                var answer = await response.Content.ReadAsStringAsync(cancellationToken);
                text = ModelWire.Text(access.Provider, answer);
                ModelWire.Record((log ?? NullLogger<HttpDrafter>.Instance), "job_advert", access.Provider, ModelWire.Usage(access.Provider, answer));
            }
            catch (JsonException)
            {
                text = null;
            }

            return text
                ?? throw new DraftUnavailable(
                    "The draft provider answers in an unexpected form.");
        }
    }
}
