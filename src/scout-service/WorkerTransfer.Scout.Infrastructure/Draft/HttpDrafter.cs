using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.Scout.Infrastructure.Draft;

/// <summary>Whether and where a model is asked at all.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Draft__ApiKey</c>, <c>Draft__BaseUrl</c>, <c>Draft__Timeout</c>,
/// <c>Draft__Model</c>, <c>Draft__Provider</c>); renaming one alone leaves its value on the default,
/// silently.
/// </remarks>
public sealed class DraftSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Draft";

    /// <summary>
    /// Empty means: no foreign service is called, and the UI says so.
    /// </summary>
    /// <remarks>
    /// The default is expressly <em>no</em> provider. Digital sovereignty means
    /// quite practically here: unless somebody deposits a key, not a word of
    /// this system leaves the system.
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The provider's address.</summary>
    /// <remarks>
    /// It belongs in the environment file and not only here: a built-in
    /// destination outside is exactly what a sovereignty report is to make
    /// visible — and the egress boundary reads its allowed hosts from the
    /// configuration.
    /// </remarks>
    public string BaseUrl { get; set; } = ModelWire.DefaultAddress;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Thirty and not five seconds: at the other end stands a language model,
    /// and a draft takes longer than a query. But not unlimited — without this
    /// line <c>HttpClient</c>'s default of a HUNDRED seconds applies, and nobody
    /// is to sit in front of a form that long.
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
/// One call, one text. No second pass, no memory, no vector store — and no row
/// anywhere saying that somebody asked (ADR-0024). <strong>And no
/// dispatch:</strong> what comes back here goes to the browser of whoever asked.
/// This service writes to nobody (ADR-0036 condition 4).
/// </remarks>
public sealed class HttpDrafter(
    IHttpClientFactory factory,
    IOptions<DraftSettings> settings,
    ICompanyAiAccess companyAi,
    ILogger<HttpDrafter>? log = null) : IDrafter
{
    private readonly DraftSettings _settings = settings.Value;

    /// <inheritdoc />
    /// <remarks>
    /// The company's own key if an admin stored one, otherwise the platform's
    /// (ADR-0051); a silent identity-service is refused, never replaced. Without
    /// either it says so instead of handing out a template that looks like a
    /// suggestion — and that somebody would send to a human being.
    /// </remarks>
    public async Task<string> DraftAsync(
        ApproachSituation situation, TenantId company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(situation);

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
            throw new DraftUnavailable("No draft provider is set up.");
        }

        // A company's own access goes through the client that reaches public
        // addresses only — its server lies under its domain, and that domain
        // must not point inside.
        using var client = factory.CreateClient(own is null ? nameof(HttpDrafter) : HttpCompanyAiAccess.ModelClientName);

        client.Timeout = _settings.Timeout;

        // What goes out stands in ApproachSituation and nowhere else: no
        // name, no address, no SubjectId, no employer, no company name — and
        // no evidence.
        using var request = ModelWire.Request(
            access.Provider, access.BaseUrl, access.Model, access.Key,
            ApproachSituation.Rules, situation.Prompt, maxTokens: 400);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // The kind, never the content. A failure is exactly the moment when
            // somebody wants to see the prompt, and exactly the one when writing
            // it down is worst.
            throw new DraftUnavailable(
                $"The draft provider does not answer ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // An expired `client.Timeout` comes as TaskCanceledException and NOT
            // as HttpRequestException — without this branch it would leave the
            // service as a 500, although it is the same statement: the provider
            // does not answer. The condition separates it from a caller who hung
            // up; that exception belongs passed on instead of turned into a 503.
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
                ModelWire.Record((log ?? NullLogger<HttpDrafter>.Instance), "scout_approach", access.Provider, ModelWire.Usage(access.Provider, answer));
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
