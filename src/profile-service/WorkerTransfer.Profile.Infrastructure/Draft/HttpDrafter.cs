using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
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
    /// Empty means: the platform has no model, and without an own key the
    /// interface says so.
    /// </summary>
    /// <remarks>
    /// The default is expressly <em>no</em> provider. Digital sovereignty means
    /// something quite practical here: unless somebody deposits a key, not a
    /// word of this system leaves the system. A local Ollama reads no key — any
    /// value switches it on (ADR-0051).
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
    /// (Ollama until the start, ADR-0051) or <c>anthropic</c>.
    /// </summary>
    public string Provider { get; set; } = ModelWire.OpenAiCompatible;
}

/// <summary>identity-service's internal door, and the paper that opens it.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Identity__BaseUrl</c>,
/// <c>Identity__Secret</c>, <c>Identity__Timeout</c>); renaming one alone
/// leaves its value on the default, silently. Empty means: no own access is
/// asked for, and the platform's model drafts.
/// </remarks>
public sealed class IdentitySettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The services' shared secret.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>How long to wait.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks a model, once, and keeps nothing.</summary>
/// <remarks>
/// <para>One call, one text. No second pass that rewrites the person's text a
/// second time unasked, and no row anywhere saying that somebody asked
/// (ADR-0024).</para>
///
/// <para><strong>Her own key first, the platform's model otherwise</strong>
/// (ADR-0051) — the same rule as the cover letter and the reply drafts. A
/// silent identity-service is refused, not quietly replaced by the platform's
/// model: she may have chosen her own provider precisely to keep her text
/// away from ours.</para>
/// </remarks>
public sealed class HttpDrafter(
    IHttpClientFactory factory,
    IOptions<DraftSettings> settings,
    IOptions<IdentitySettings> identity) : IDrafter
{
    /// <summary>The client's name for the internal door.</summary>
    public const string IdentityClient = "identity-internal";

    private readonly DraftSettings _settings = settings.Value;
    private readonly IdentitySettings _identity = identity.Value;

    /// <inheritdoc />
    public async Task<string> DraftAsync(
        DraftSituation situation,
        SubjectId requestedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(situation);

        var access = await OwnAccessAsync(requestedBy, cancellationToken) ?? Platform()
            ?? throw new DraftUnavailable("No draft provider is set up.");

        using var client = factory.CreateClient(nameof(HttpDrafter));

        client.Timeout = _settings.Timeout;

        // What goes out stands in DraftSituation and nowhere else: no name,
        // no address, no SubjectId, no employer.
        using var request = ModelWire.Request(
            access.Provider, access.BaseUrl, access.Model, access.Key,
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
                    access.Provider, await response.Content.ReadAsStringAsync(cancellationToken));
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

    private AiAccessV1? Platform() =>
        _settings.ApiKey.Length == 0
            ? null
            : new AiAccessV1(_settings.Provider, _settings.BaseUrl, _settings.Model, _settings.ApiKey);

    /// <summary>Her own access — or <c>null</c> when she set none up.</summary>
    private async Task<AiAccessV1?> OwnAccessAsync(SubjectId subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_identity.BaseUrl) || string.IsNullOrEmpty(_identity.Secret))
        {
            return null;
        }

        using var client = factory.CreateClient(IdentityClient);
        client.Timeout = _identity.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_identity.BaseUrl), $"/internal/account/{subject.Value}/ai"));
        request.Headers.Add("X-Notify-Secret", _identity.Secret);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException
                                          || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new DraftUnavailable("identity-service (internal) is not reachable.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DraftUnavailable($"identity-service (internal) answers with {(int)response.StatusCode}.");
            }

            AiAccessV1? access;

            try
            {
                access = await response.Content.ReadFromJsonAsync<AiAccessV1>(cancellationToken);
            }
            catch (JsonException)
            {
                throw new DraftUnavailable("identity-service (internal) sent an unusable answer.");
            }

            return access is { Key.Length: > 0 } && ModelWire.IsKnown(access.Provider) ? access : null;
        }
    }
}
