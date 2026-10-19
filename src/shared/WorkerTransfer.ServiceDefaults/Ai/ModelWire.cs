using System.Net.Http.Json;
using System.Text.Json;

namespace WorkerTransfer.ServiceDefaults.Ai;

/// <summary>
/// The two wire formats a one-shot draft speaks: OpenAI-compatible and
/// Anthropic.
/// </summary>
/// <remarks>
/// <para><strong>OpenAI-compatible is the default, because Mistral speaks
/// it</strong> (VISION-FRAGEN E2: Mistral, EU, is the platform's provider;
/// Ollama and anything else of that shape too). Anthropic stays selectable.
/// The label is the same one a person's own access carries
/// (<c>AiAccessV1.Provider</c>), so the evidence report counts both in one
/// vocabulary.</para>
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

    /// <summary>The platform's default endpoint: Mistral, in the EU.</summary>
    public const string DefaultAddress = "https://api.mistral.ai/v1/chat/completions";

    /// <summary>The platform's default model.</summary>
    public const string DefaultModel = "mistral-medium-latest";

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
                        max_tokens = maxTokens,
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

        return root.TryGetProperty("content", out var blocks)
               && blocks.ValueKind == JsonValueKind.Array
               && blocks.GetArrayLength() > 0
               && blocks[0].TryGetProperty("text", out var text)
               && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }
}
