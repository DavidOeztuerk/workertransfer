using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.Applications.Infrastructure.CoverLetter;

/// <summary>Whether and where a model is asked.</summary>
/// <remarks>
/// The same section <c>Draft</c> as in jobs-service and profile-service: one
/// platform, one provider, one key. Two sections would be two chances to forget
/// one of them.
/// <para>
/// The property names ARE the environment keys (<c>Draft__ApiKey</c>,
/// <c>Draft__BaseUrl</c>, <c>Draft__Timeout</c>,
/// <c>Draft__Model</c>, <c>Draft__Provider</c>); renaming one alone leaves its value on the default,
/// silently.
/// </para>
/// </remarks>
public sealed class CoverLetterSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Draft";

    /// <summary>Empty means: nothing is called, and the interface says so.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The provider's address.</summary>
    public string BaseUrl { get; set; } = ModelWire.DefaultAddress;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Sixty and not thirty seconds as for the advert draft: a cover letter is
    /// longer and carries more context. But not unlimited either — without this
    /// line <c>HttpClient</c>'s default applies with a HUNDRED seconds.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Which model.</summary>
    public string Model { get; set; } = ModelWire.DefaultModel;

    /// <summary>
    /// Which wire format the operator's address speaks: <c>openai_compatible</c>
    /// (Mistral, the default) or <c>anthropic</c>.
    /// </summary>
    public string Provider { get; set; } = ModelWire.OpenAiCompatible;
}

/// <summary>The cover-letter writer when none is set up.</summary>
/// <remarks>
/// It delivers no template but says that it does not exist. A cover letter that
/// does not come from the model but looks like it would be the worse answer —
/// and in the state "review" an invitation to approve it by mistake (ADR-0034).
/// </remarks>
public sealed class NoCoverLetterWriter : ICoverLetterWriter
{
    /// <inheritdoc />
    public Task<string> WriteAsync(
        AiAccess access,
        CoverLetterContext context,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(context);
        _ = progress;
        _ = cancellationToken;

        return Task.FromException<string>(
            new CoverLetterUnavailable(CoverLetterUnavailable.NoProvider));
    }

    /// <inheritdoc />
    public Task<string> ReviseAsync(
        AiAccess access,
        CoverLetterContext context,
        string subjectLine,
        string text,
        IReadOnlyList<string> comments,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(comments);
        _ = subjectLine;
        _ = text;
        _ = progress;
        _ = cancellationToken;

        return Task.FromException<string>(
            new CoverLetterUnavailable(CoverLetterUnavailable.NoProvider));
    }
}

/// <summary>Asks a model — on request, once, and keeps nothing.</summary>
/// <remarks>
/// <strong>What is stored is the result</strong>, because it is the document
/// (ADR-0034). The input to the model is not: no prompt archive, no embeddings,
/// no memory across applications. A model that had "learnt from the last twenty
/// applications" would make statements about the person she never made.
/// <para>
/// <strong>The prompt text below stays German</strong>, like
/// <see cref="CoverLetterContext.Rules"/>: its labels and the <c>BETREFF:</c>
/// marker are what the model was measured against, and the language of the
/// letter is set by a line in it, not by the language of the prompt.
/// </para>
/// </remarks>
public sealed class HttpCoverLetterWriter(
    IHttpClientFactory factory, IOptions<CoverLetterSettings> settings) : ICoverLetterWriter
{
    /// <summary>The name under which the client is registered.</summary>
    public const string ClientName = "cover-letter";

    /// <summary>
    /// Upper bound of the answer. Streaming carries the waiting time; 1200 tokens
    /// suffice for 250 words. 1500 used to push a local 7B beyond the minute,
    /// before the stream arrived piece by piece.
    /// </summary>
    public const int MaxTokens = 1200;

    private readonly CoverLetterSettings _settings = settings.Value;

    /// <inheritdoc />
    public Task<string> WriteAsync(
        AiAccess access,
        CoverLetterContext context,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(context);

        return AskMaskedAsync(access, context, Instruction(context), progress, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> ReviseAsync(
        AiAccess access,
        CoverLetterContext context,
        string subjectLine,
        string text,
        IReadOnlyList<string> comments,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(comments);

        var builder = new StringBuilder(Instruction(context));

        builder.AppendLine().AppendLine("## Das bisherige Anschreiben");
        builder.AppendLine($"BETREFF: {subjectLine}");
        builder.AppendLine("---");
        builder.AppendLine(text);
        builder.AppendLine();
        builder.AppendLine("## Anmerkungen des Bewerbers — ALLE umsetzen");
        builder.AppendLine(
            "Behalte alles bei, was nicht angemerkt wurde. Tonfall und Sprache bleiben.");

        for (var number = 0; number < comments.Count; number++)
        {
            builder.AppendLine($"{number + 1}. {comments[number]}");
        }

        return AskMaskedAsync(access, context, builder.ToString(), progress, cancellationToken);
    }

    /// <summary>
    /// What the platform knows as a field goes out as a placeholder (E2): the
    /// person's name, the addressee, every former employer.
    /// </summary>
    /// <remarks>
    /// Masked HERE, at the last step before the wire, so no caller can forget
    /// it. The whole instruction is masked — the name also stands in the
    /// person's own text, the employer in the job description.
    /// </remarks>
    public static Placeholders PlaceholdersFor(CoverLetterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var placeholders = new Placeholders();
        placeholders.AddPersonName(context.OwnName);
        placeholders.Add("COMPANY", context.Company);

        foreach (var station in context.OwnCareer)
        {
            placeholders.Add("EMPLOYER", station.Employer, numbered: true);
        }

        return placeholders;
    }

    private async Task<string> AskMaskedAsync(
        AiAccess access,
        CoverLetterContext context,
        string instruction,
        Func<string, Task>? progress,
        CancellationToken cancellationToken)
    {
        var placeholders = PlaceholdersFor(context);

        Func<string, Task>? restoring = progress is null
            ? null
            : fragment => progress(placeholders.RestoreFragment(fragment));

        var answer = await AskAsync(
            access, placeholders.Mask(instruction), restoring, cancellationToken);

        return placeholders.Restore(answer);
    }

    /// <summary>The instruction, from the context and nothing else.</summary>
    private static string Instruction(CoverLetterContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Die Stelle (aus der öffentlichen Anzeige)");
        builder.AppendLine($"- Titel: {context.JobTitle}");
        builder.AppendLine($"- Unternehmen: {context.Company}");
        builder.AppendLine($"- Ort: {context.JobLocation}");
        builder.AppendLine($"- Gesucht: {string.Join(", ", context.RequestedSkills)}");
        builder.AppendLine($"- Beschreibung: {context.JobDescription}");
        builder.AppendLine();
        builder.AppendLine("## Die bewerbende Person (ihre eigenen Angaben)");
        builder.AppendLine($"- Voller Name (Signatur, GENAU so): {context.OwnName}");
        builder.AppendLine($"- Überschrift: {context.OwnHeadline}");
        builder.AppendLine($"- Über sich: {context.OwnText}");
        builder.AppendLine($"- Genannte Fähigkeiten: {string.Join(", ", context.OwnSkills)}");
        builder.AppendLine($"- Sprache: {context.Language} (in dieser Sprache, grammatisch korrekt)");
        builder.AppendLine();
        builder.AppendLine("## Werdegang");

        if (context.OwnCareer.Count == 0)
        {
            builder.AppendLine("(kein Werdegang hinterlegt — erfinde keinen)");
        }

        foreach (var station in context.OwnCareer)
        {
            builder.AppendLine(
                $"- {station.Period}: {station.Title} bei {station.Employer}"
                + $" ({string.Join(", ", station.Technologies)}) — {station.Description}");
        }

        return builder.ToString();
    }

    private async Task<string> AskAsync(
        AiAccess access,
        string instruction,
        Func<string, Task>? progress,
        CancellationToken cancellationToken)
    {
        if (!access.IsSetUp)
        {
            throw new CoverLetterUnavailable(CoverLetterUnavailable.NoProvider);
        }

        using var client = factory.CreateClient(ClientName);

        // With ResponseHeadersRead this limit applies until the first byte, not
        // until the last token. Otherwise a local model dies after a minute
        // although it has already written — measured against Ollama.
        client.Timeout = _settings.Timeout;

        using var request = BuildRequest(access, instruction);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new CoverLetterUnavailable(
                $"The draft provider does not answer ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CoverLetterUnavailable(
                "The draft provider does not answer (timeout).");
        }

        using (response)
        {
            // A redirect is NOT followed, deliberately. The request carries the
            // person's key and their profile text; a 307/308 would resend both
            // to wherever the Location points. Since Noelia 7.0.0 the client
            // hands the redirect back instead of following it, and the person
            // enters the address it points to.
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                throw new CoverLetterUnavailable(
                    $"The draft provider redirects ({(int)response.StatusCode}); "
                    + "the address has to be the one it redirects to.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CoverLetterUnavailable(
                    $"The draft provider answers with {(int)response.StatusCode}.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var text = await ReadStreamAsync(
                    access.Provider, stream, _settings.Timeout,
                    progress, cancellationToken);

                return text is { Length: > 0 }
                    ? text
                    : throw new CoverLetterUnavailable(
                        "The draft provider sent an empty text.");
            }
            catch (CoverLetterUnavailable)
            {
                throw;
            }
            catch (Exception error)
                when (error is JsonException or KeyNotFoundException or IndexOutOfRangeException
                      or InvalidOperationException)
            {
                throw new CoverLetterUnavailable(
                    "The draft provider sent an unusable or empty response.");
            }
        }
    }

    private static HttpRequestMessage BuildRequest(AiAccess access, string instruction)
    {
        // The anonymous objects below ARE the providers' wire format: their
        // member names become the JSON keys.
        if (access.Provider == "openai_compatible")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, access.Address)
            {
                Content = JsonContent.Create(new
                {
                    model = access.Model,
                    max_tokens = MaxTokens,
                    stream = true,
                    messages = new[]
                    {
                        new { role = "system", content = CoverLetterContext.Rules },
                        new { role = "user", content = instruction }
                    }
                })
            };

            if (access.Key.Length > 0)
            {
                request.Headers.TryAddWithoutValidation(
                    "Authorization", $"Bearer {access.Key}");
            }

            return request;
        }

        var anthropic = new HttpRequestMessage(HttpMethod.Post, access.Address)
        {
            Content = JsonContent.Create(new
            {
                model = access.Model,
                max_tokens = MaxTokens,
                stream = true,
                system = CoverLetterContext.Rules,
                messages = new[] { new { role = "user", content = instruction } }
            })
        };

        anthropic.Headers.Add("x-api-key", access.Key);
        anthropic.Headers.Add("anthropic-version", "2023-06-01");
        return anthropic;
    }

    /// <summary>
    /// Reads server-sent events or a single JSON — OpenAI-compatible and
    /// Anthropic. One token after the other, not only the whole.
    /// </summary>
    /// <remarks>
    /// Between two lines there may be a pause as long as the time limit, not for
    /// the whole letter. A local model that writes slowly, but writes, may
    /// finish. If nothing comes any more and a cover letter already lies there,
    /// the piece counts — otherwise a minute of writing would again be an empty
    /// failure.
    /// </remarks>
    public static async Task<string> ReadStreamAsync(
        string provider,
        Stream stream,
        TimeSpan stall,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(stream);

        using var stallToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // The first token may take longer than the pause between two. Empty SSE
        // lines must NOT reset the clock — otherwise a mute stream runs for
        // minutes without a single letter ever arriving.
        stallToken.CancelAfter(stall);

        using var reader = new StreamReader(stream);
        var text = new StringBuilder();
        var raw = new StringBuilder();

        try
        {
            while (await reader.ReadLineAsync(stallToken.Token) is { } row)
            {
                if (row.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = row["data:".Length..].Trim();

                    if (payload.Length == 0)
                    {
                        continue;
                    }

                    if (payload == "[DONE]")
                    {
                        break;
                    }

                    await TakeDeltaAsync(
                        provider, payload, text, stallToken, stall, progress);
                    continue;
                }

                if (row.StartsWith("event:", StringComparison.OrdinalIgnoreCase)
                    || row.StartsWith(':')
                    || row.Length == 0)
                {
                    continue;
                }

                // Ollama native and other NDJSON providers: one JSON line per
                // token, without a `data:` prefix. Without this branch
                // everything lands in `raw` and the letter appears only at the
                // end.
                if (row[0] == '{')
                {
                    await TakeDeltaAsync(
                        provider, row, text, stallToken, stall, progress);
                    continue;
                }

                raw.Append(row);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (text.Length > 0)
            {
                return text.ToString();
            }

            throw new CoverLetterUnavailable(
                "The draft provider does not answer (timeout).");
        }

        if (text.Length > 0)
        {
            return text.ToString();
        }

        if (raw.Length == 0)
        {
            return string.Empty;
        }

        using var parsed = JsonDocument.Parse(raw.ToString());

        return ReadText(provider, parsed.RootElement) ?? string.Empty;
    }

    private static async Task TakeDeltaAsync(
        string provider,
        string json,
        StringBuilder text,
        CancellationTokenSource stallToken,
        TimeSpan stall,
        Func<string, Task>? progress)
    {
        string? piece;

        try
        {
            piece = ReadDelta(provider, json);
        }
        catch (JsonException)
        {
            return;
        }

        if (piece is not { Length: > 0 })
        {
            return;
        }

        text.Append(piece);
        stallToken.CancelAfter(stall);

        if (progress is not null)
        {
            await progress(text.ToString());
        }
    }

    /// <summary>A token from an SSE piece — or null when there is none in it.</summary>
    public static string? ReadDelta(string provider, string json)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrEmpty(json);

        using var parsed = JsonDocument.Parse(json);

        return ReadDelta(provider, parsed.RootElement);
    }

    private static string? ReadDelta(string provider, JsonElement root)
    {
        if (provider == "openai_compatible")
        {
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];

                if (first.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }

                if (first.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var whole)
                    && whole.ValueKind == JsonValueKind.String)
                {
                    return whole.GetString();
                }
            }

            // Ollama `/api/chat`: { "message": { "content": "…" }, "done": false }
            if (root.TryGetProperty("message", out var ollama)
                && ollama.TryGetProperty("content", out var ollamaText)
                && ollamaText.ValueKind == JsonValueKind.String)
            {
                return ollamaText.GetString();
            }

            // Ollama `/api/generate`: { "response": "…", "done": false }
            if (root.TryGetProperty("response", out var generated)
                && generated.ValueKind == JsonValueKind.String)
            {
                return generated.GetString();
            }

            return null;
        }

        if (root.TryGetProperty("type", out var type)
            && type.GetString() == "content_block_delta"
            && root.TryGetProperty("delta", out var anthropic)
            && anthropic.TryGetProperty("text", out var piece)
            && piece.ValueKind == JsonValueKind.String)
        {
            return piece.GetString();
        }

        return null;
    }

    private static string? ReadText(string provider, JsonElement root)
    {
        if (provider == "openai_compatible")
        {
            return root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        return root.GetProperty("content")[0].GetProperty("text").GetString();
    }
}
