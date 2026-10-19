using System.Net.Http.Json;
using System.Text.Json;

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
}
