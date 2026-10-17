using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Messages.Infrastructure.Companies;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.Messages.Infrastructure.Draft;

/// <summary>The platform's model — the operator's local Ollama until the start (ADR-0051).</summary>
/// <remarks>
/// The same section <c>Draft</c> as in the four other seams. The property names
/// ARE the environment keys (<c>Draft__ApiKey</c>, <c>Draft__BaseUrl</c>,
/// <c>Draft__Timeout</c>, <c>Draft__Model</c>, <c>Draft__Provider</c>);
/// renaming one alone leaves its value on the default, silently.
/// </remarks>
public sealed class DraftSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Draft";

    /// <summary>Empty means: the platform's model is not called, and the UI says so.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The provider's address.</summary>
    public string BaseUrl { get; set; } = ModelWire.DefaultAddress;

    /// <summary>How long to wait — a reply is short, but a local model is slow.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Which model.</summary>
    public string Model { get; set; } = ModelWire.DefaultModel;

    /// <summary><c>openai_compatible</c> (the default) or <c>anthropic</c>.</summary>
    public string Provider { get; set; } = ModelWire.OpenAiCompatible;

}

/// <summary>Drafts a reply — once, on request, and keeps nothing.</summary>
/// <remarks>
/// <para><strong>Which model:</strong> on the person's side their own access
/// from the account settings, if they set one up — a person who chose a local
/// model chose it for every draft, not only for the cover letter. Otherwise,
/// and always on the company's side, the platform's.</para>
///
/// <para><strong>What leaves:</strong> <see cref="ReplyContext"/> and nothing
/// else, with the person's name masked (E2). A redirect is not followed — the
/// request carries a key and the conversation.</para>
/// </remarks>
public sealed class HttpReplyDrafter(
    IHttpClientFactory factory,
    IOptions<DraftSettings> settings,
    IOptions<IdentitySettings> identity,
    ILogger<HttpReplyDrafter> log) : IReplyDrafter, IAdvisor
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "reply-draft";

    /// <summary>Upper bound of the answer — 150 words fit.</summary>
    public const int MaxTokens = 500;

    private readonly DraftSettings _settings = settings.Value;
    private readonly IdentitySettings _identity = identity.Value;

    /// <inheritdoc />
    public async Task<string> DraftAsync(
        ReplyContext context, SubjectId requestedBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var access = context.Writing == Side.Person
            ? await OwnAccessAsync(requestedBy, cancellationToken) ?? Platform()
            : Platform();

        return await AskAsync(access, ReplyContext.Rules, Prompt(context), context.PersonName, MaxTokens, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The person's side, always: the advisor works for her, so it speaks with
    /// her own access if she set one up (ADR-0049).
    /// </remarks>
    public async Task<string> AdviseAsync(
        AdviceContext context, SubjectId requestedBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var access = await OwnAccessAsync(requestedBy, cancellationToken) ?? Platform();

        return await AskAsync(access, AdviceContext.Rules, AdvicePrompt(context), context.PersonName, AdviceTokens, cancellationToken);
    }

    /// <summary>Upper bound of the advice — 200 words fit.</summary>
    public const int AdviceTokens = 700;

    /// <summary>One call, masked on the way out, restored on the way back.</summary>
    private async Task<string> AskAsync(
        AiAccessV1? access, string rules, string prompt, string personName, int maxTokens,
        CancellationToken cancellationToken)
    {
        if (access is null)
        {
            throw new DraftUnavailable(DraftUnavailable.NoProvider);
        }

        var placeholders = new Placeholders();
        placeholders.AddPersonName(personName);

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = ModelWire.Request(
            access.Provider, access.BaseUrl, access.Model, access.Key,
            rules, placeholders.Mask(prompt), maxTokens);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // The kind, never the content.
            throw new DraftUnavailable($"The draft provider does not answer ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DraftUnavailable("The draft provider does not answer (timeout).");
        }

        using (response)
        {
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                throw new DraftUnavailable(
                    $"The draft provider redirects ({(int)response.StatusCode}); "
                    + "the address has to be the one it redirects to.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DraftUnavailable($"The draft provider answers with {(int)response.StatusCode}.");
            }

            string? text;

            try
            {
                text = ModelWire.Text(access.Provider, await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (JsonException)
            {
                text = null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new DraftUnavailable("The draft provider sent an empty text.");
            }

            var restored = placeholders.Restore(text).Trim();

            return restored.Length > Message.MaxLength ? restored[..Message.MaxLength] : restored;
        }
    }

    /// <summary>The advisor's prompt — the conversation and her own facts, each with its source.</summary>
    /// <remarks>
    /// A missing or unreadable fact is written out as such: the rules tell the
    /// model to say so instead of guessing (ADR-0022 §3).
    /// </remarks>
    public static string AdvicePrompt(AdviceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();
        builder.AppendLine($"Sprache: {context.Language}");
        builder.AppendLine();
        builder.AppendLine("## Ihre eigenen Angaben");

        var wishes = context.Facts.Wishes;

        builder.AppendLine(context.Facts.WishesUnknown
            ? "- Mandat: nicht abrufbar"
            : wishes is null
                ? "- Mandat: keines angelegt"
                : $"- Mandat: Eintritt {wishes.EntryMonth ?? "nicht angegeben"}, "
                  + $"Gehaltswunsch {Range(wishes.SalaryMin, wishes.SalaryMax)}, "
                  + $"Pensum {(wishes.WorkloadPercent is { } workload ? $"{workload} %" : "nicht angegeben")} (dein Mandat)");

        builder.AppendLine(context.Facts.NoticeUnknown
            ? "- Fruehester Wechsel: nicht abrufbar"
            : context.Facts.EarliestStart is { } earliest
                ? $"- Fruehester Wechsel bei Kuendigung heute: {earliest:yyyy-MM-dd} (deine Kuendigungsfrist)"
                : "- Fruehester Wechsel: keine Kuendigungsfrist angegeben");

        builder.AppendLine();
        builder.AppendLine("## Das Gespraech (aelteste zuerst)");

        if (context.History.Count == 0)
        {
            builder.AppendLine("(noch keine Nachricht)");
        }

        foreach (var entry in context.History)
        {
            builder.AppendLine(entry.Side == Side.Person ? "### Person ([PERSON_NAME])" : "### Unternehmen");
            builder.AppendLine(entry.Text);
        }

        return builder.ToString();
    }

    private static string Range(int? minimum, int? maximum) =>
        (minimum, maximum) switch
        {
            (null, null) => "nicht angegeben",
            ({ } low, null) => $"ab {low}",
            (null, { } high) => $"bis {high}",
            ({ } low, { } high) => $"{low} bis {high}"
        };

    /// <summary>The prompt, from the context and nothing else.</summary>
    /// <remarks>German, like <see cref="ReplyContext.Rules"/>.</remarks>
    public static string Prompt(ReplyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();
        builder.AppendLine(context.Writing == Side.Person
            ? "Du schreibst fuer die Person, die sich beworben hat ([PERSON_NAME])."
            : "Du schreibst fuer das Unternehmen, bei dem sich [PERSON_NAME] beworben hat.");
        builder.AppendLine($"Sprache ohne bisheriges Gespraech: {context.Language}");
        builder.AppendLine();
        builder.AppendLine("## Das bisherige Gespraech (aelteste zuerst)");

        if (context.History.Count == 0)
        {
            builder.AppendLine("(noch keine Nachricht — schreib eine erste, kurze Nachricht)");
        }

        foreach (var entry in context.History)
        {
            builder.AppendLine(entry.Side == Side.Person ? "### Person" : "### Unternehmen");
            builder.AppendLine(entry.Text);
        }

        return builder.ToString();
    }

    private AiAccessV1? Platform() =>
        _settings.ApiKey.Length == 0
            ? null
            : new AiAccessV1(_settings.Provider, _settings.BaseUrl, _settings.Model, _settings.ApiKey);

    /// <summary>The person's own access — or <c>null</c> when they set none up.</summary>
    /// <remarks>
    /// A silent identity-service is NOT "none set up": falling back to the
    /// platform then would send the text to a provider the person may have
    /// chosen against. It is refused instead.
    /// </remarks>
    private async Task<AiAccessV1?> OwnAccessAsync(SubjectId subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_identity.BaseUrl) || string.IsNullOrEmpty(_identity.Secret))
        {
            return null;
        }

        using var client = factory.CreateClient(HttpCompanyMemberLookup.ClientName);
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
            log.LogWarning("identity-service (AI access) unreachable ({Kind})", error.GetType().Name);
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
                throw new DraftUnavailable(
                    $"identity-service (internal) answers with {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<AiAccessV1>(cancellationToken);

            if (body is null || !ModelWire.IsKnown(body.Provider ?? string.Empty)
                || string.IsNullOrEmpty(body.BaseUrl) || string.IsNullOrEmpty(body.Model)
                || (body.Provider == ModelWire.Anthropic && string.IsNullOrEmpty(body.Key)))
            {
                return null;
            }

            return body with
            {
                BaseUrl = ForTheContainer(Complete(body.BaseUrl, body.Provider)),
                Key = body.Key ?? string.Empty
            };
        }
    }

    /// <summary>A bare Ollama address without a path gets the OpenAI path.</summary>
    public static string Complete(string address, string? provider)
    {
        var trimmed = address.Trim().TrimEnd('/');

        if (provider != ModelWire.OpenAiCompatible || trimmed.Length == 0
            || trimmed.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/v1/chat/completions";
    }

    /// <summary>Inside the container localhost is the container; Ollama runs on the machine.</summary>
    public static string ForTheContainer(string address) =>
        !string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
            StringComparison.OrdinalIgnoreCase)
            ? address
            : address
                .Replace("://localhost", "://host.docker.internal", StringComparison.OrdinalIgnoreCase)
                .Replace("://127.0.0.1", "://host.docker.internal", StringComparison.Ordinal);
}
