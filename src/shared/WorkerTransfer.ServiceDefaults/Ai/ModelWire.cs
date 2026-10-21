using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.ServiceDefaults.Ai;

/// <summary>
/// The two wire formats a one-shot draft speaks: OpenAI-compatible and
/// Anthropic.
/// </summary>
/// <remarks>
/// <para><strong>OpenAI-compatible is the default, because the platform's
/// model speaks it</strong> — until the start the operator's local Ollama
/// (ADR-0051), and Mistral, OpenAI and anything else of that shape too.
/// Anthropic has its own format. The label is the same one a person's own
/// access carries (<c>AiAccessV1.Provider</c>), so the evidence report counts
/// both in one vocabulary.</para>
///
/// <para>Only the format lives here. What goes into the prompt is decided by
/// each service's own context type (ADR-0024) and never by this class.</para>
/// </remarks>
public static class ModelWire
{
    /// <summary>The label for OpenAI-compatible endpoints — Mistral, Ollama.</summary>
    public const string OpenAiCompatible = "openai_compatible";

    /// <summary>The label for Anthropic's messages endpoint.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>The platform's default endpoint until the start: the operator's local Ollama (ADR-0051).</summary>
    /// <remarks>Nothing leaves the machine. Compose sets it again from <c>WORKERTRANSFER_AI_URL</c>.</remarks>
    public const string DefaultAddress = "http://host.docker.internal:11434/v1/chat/completions";

    /// <summary>The platform's default model — one the operator has pulled into Ollama.</summary>
    public const string DefaultModel = "qwen2.5-coder:7b";

    /// <summary>The <c>max_tokens</c> an Anthropic request carries at least.</summary>
    /// <remarks>
    /// Current Claude models think before they write, and the thinking counts
    /// against <c>max_tokens</c>. With the few hundred tokens a draft needs,
    /// the thinking alone could use them up and the answer would carry no
    /// text. Only what is generated is billed, so the room costs nothing.
    /// </remarks>
    public const int AnthropicRoom = 16_000;

    /// <summary>How hard an Anthropic model thinks about a draft: a short text needs little.</summary>
    public const string AnthropicEffort = "low";

    /// <summary>Whether the label is one this class can speak.</summary>
    public static bool IsKnown(string provider) =>
        provider is OpenAiCompatible or Anthropic;

    /// <summary>A single, non-streamed request.</summary>
    /// <exception cref="ArgumentException">An unknown provider label.</exception>
    public static HttpRequestMessage Request(
        string provider, string address, string model, string key,
        string system, string user, int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(key);

        // The anonymous objects ARE the providers' wire formats: their member
        // names become the JSON keys.
        switch (provider)
        {
            case OpenAiCompatible:
            {
                var request = new HttpRequestMessage(HttpMethod.Post, address)
                {
                    Content = JsonContent.Create(new
                    {
                        model,
                        max_tokens = maxTokens,
                        messages = new[]
                        {
                            new { role = "system", content = system },
                            new { role = "user", content = user }
                        }
                    })
                };

                if (key.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
                }

                return request;
            }

            case Anthropic:
            {
                var request = new HttpRequestMessage(HttpMethod.Post, address)
                {
                    Content = JsonContent.Create(new
                    {
                        model,
                        max_tokens = Math.Max(maxTokens, AnthropicRoom),
                        output_config = new { effort = AnthropicEffort },
                        system,
                        messages = new[] { new { role = "user", content = user } }
                    })
                };

                request.Headers.Add("x-api-key", key);
                request.Headers.Add("anthropic-version", "2023-06-01");
                return request;
            }

            default:
                throw new ArgumentException($"Unknown provider label '{provider}'.", nameof(provider));
        }
    }

    /// <summary>The text of a non-streamed answer, or null when it has none.</summary>
    public static string? Text(string provider, string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        if (provider == OpenAiCompatible)
        {
            return root.TryGetProperty("choices", out var choices)
                   && choices.ValueKind == JsonValueKind.Array
                   && choices.GetArrayLength() > 0
                   && choices[0].TryGetProperty("message", out var message)
                   && message.TryGetProperty("content", out var content)
                   && content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : null;
        }

        // A refusal carries no draft, whatever else stands in the answer.
        if (root.TryGetProperty("stop_reason", out var stop)
            && stop.ValueKind == JsonValueKind.String
            && stop.GetString() == "refusal")
        {
            return null;
        }

        if (!root.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // The first TEXT block, not the first block: current models put a
        // thinking block in front, and its text field is empty or absent.
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "text"
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }
        }

        return null;
    }

    /// <summary>The tokens of a non-streamed answer, or null when the provider did not report them.</summary>
    /// <remarks>
    /// OpenAI-compatible: <c>usage.prompt_tokens</c> / <c>usage.completion_tokens</c>;
    /// Anthropic: <c>usage.input_tokens</c> / <c>usage.output_tokens</c> — the
    /// thinking counts as output, which is what is billed.
    /// </remarks>
    public static TokenUsage? Usage(string provider, string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        using var parsed = JsonDocument.Parse(json);
        return UsageOf(provider, parsed.RootElement);
    }

    /// <summary>
    /// What one server-sent event adds to the tokens counted so far.
    /// </summary>
    /// <remarks>
    /// OpenAI-compatible streams report the usage in their last chunk, and only
    /// when the request asked for it (<c>stream_options.include_usage</c>).
    /// Anthropic splits it: the input arrives with <c>message_start</c>, the
    /// output with <c>message_delta</c>. A payload that is no JSON or carries
    /// nothing leaves the count as it was.
    /// </remarks>
    public static TokenUsage? StreamUsage(string provider, string payload, TokenUsage? sofar)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload[0] != '{')
        {
            return sofar;
        }

        try
        {
            using var parsed = JsonDocument.Parse(payload);
            var root = parsed.RootElement;

            if (provider == Anthropic && root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                var current = sofar ?? new TokenUsage(0, 0);
                if (type.GetString() == "message_start"
                    && root.TryGetProperty("message", out var message)
                    && message.TryGetProperty("usage", out var startUsage))
                {
                    return current with { Input = Count(startUsage, "input_tokens") ?? current.Input };
                }

                if (type.GetString() == "message_delta" && root.TryGetProperty("usage", out var deltaUsage))
                {
                    return current with { Output = Count(deltaUsage, "output_tokens") ?? current.Output };
                }

                return sofar;
            }

            return UsageOf(provider, root) ?? sofar;
        }
        catch (JsonException)
        {
            return sofar;
        }
    }

    /// <summary>
    /// Writes down what one model call cost — the numbers and the agent, never
    /// a word of what was asked or answered.
    /// </summary>
    /// <remarks>
    /// <strong>This line IS the measurement</strong> the prices rest on
    /// (<c>docs/GESCHAEFTSMODELL-FRAGEN.md</c>): one per call, per agent. A
    /// metrics instrument would be the tidier place, but nothing here exports
    /// custom meters, and an instrument nobody reads is the "registered,
    /// present, without effect" this repository keeps finding.
    /// </remarks>
    public static void Record(ILogger logger, string agent, string provider, TokenUsage? usage)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (usage is not { } counted)
        {
            logger.LogInformation(
                "Model call {Agent} via {ProviderLabel}: the provider reported no token usage", agent, provider);
            return;
        }

        logger.LogInformation(
            "Model call {Agent} via {ProviderLabel}: {InputTokens} tokens in, {OutputTokens} tokens out",
            agent, provider, counted.Input, counted.Output);
    }

    private static TokenUsage? UsageOf(string provider, JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var (inKey, outKey) = provider == Anthropic
            ? ("input_tokens", "output_tokens")
            : ("prompt_tokens", "completion_tokens");

        return Count(usage, inKey) is { } input && Count(usage, outKey) is { } output
            ? new TokenUsage(input, output)
            : null;
    }

    private static int? Count(JsonElement usage, string key) =>
        usage.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var count)
            ? count
            : null;
}

/// <summary>The tokens one model call consumed — numbers only.</summary>
/// <param name="Input">What went in: instruction and context.</param>
/// <param name="Output">What came out, the thinking included.</param>
public readonly record struct TokenUsage(int Input, int Output);

