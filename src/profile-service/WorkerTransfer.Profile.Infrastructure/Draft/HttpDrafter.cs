using System.Text.Json;
using Microsoft.Extensions.Options;
using WorkerTransfer.ServiceDefaults.Ai;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Draft;

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
    /// <remarks>
    /// The default is expressly <em>no</em> provider. Digital sovereignty means
    /// something quite practical here: unless somebody deposits a key, not a
    /// word of this system leaves the system.
    /// </remarks>
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
    /// (Mistral, the default) or <c>anthropic</c>.
    /// </summary>
    public string Provider { get; set; } = ModelWire.OpenAiCompatible;
}

/// <summary>The drafter when none is set up.</summary>
/// <remarks>
/// It is not silent and does not deliver a template either — it says that it
/// does not exist. A draft that does not come from the model but looks like it
/// would be the worse answer: the person would take it for a suggestion and
/// could not know that nobody was asked.
/// </remarks>
public sealed class NoDrafter : IDrafter
{
    /// <inheritdoc />
    public Task<string> DraftAsync(
        DraftSituation situation, CancellationToken cancellationToken = default) =>
        throw new DraftUnavailable("No draft provider is set up.");
}

/// <summary>Asks a model, once, and keeps nothing.</summary>
/// <remarks>
/// One call, one text. No second pass that rewrites the person's text a second
/// time unasked, and no row anywhere saying that somebody asked (ADR-0024).
/// </remarks>
public sealed class HttpDrafter(
    IHttpClientFactory factory,
    IOptions<DraftSettings> settings) : IDrafter
{
    private readonly DraftSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string> DraftAsync(
        DraftSituation situation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(situation);

        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            throw new DraftUnavailable("No draft provider is set up.");
        }

        using var client = factory.CreateClient(nameof(HttpDrafter));

        client.Timeout = _settings.Timeout;

        // What goes out stands in DraftSituation and nowhere else: no name,
        // no address, no SubjectId, no employer.
        using var request = ModelWire.Request(
            _settings.Provider, _settings.BaseUrl, _settings.Model, _settings.ApiKey,
            DraftSituation.Rules, situation.Prompt, maxTokens: 400);

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
                text = ModelWire.Text(
                    _settings.Provider, await response.Content.ReadAsStringAsync(cancellationToken));
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
