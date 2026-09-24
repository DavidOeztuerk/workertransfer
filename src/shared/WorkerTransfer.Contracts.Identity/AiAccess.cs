using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>
/// A person's AI access, as it travels internally from identity-service to the
/// services that ask a model on request.
/// </summary>
/// <remarks>
/// <strong>Only over the internal wire, never through the gateway.</strong> The
/// key stands here in clear text, because the receiver has to send it to the
/// provider — and exactly for that reason this shape must never reach the
/// browser. <c>GET /account/settings</c> deliberately does not carry it.
/// <para>
/// <c>provider</c> is the label from the account settings: <c>none</c>,
/// <c>openai_compatible</c> (Ollama, vLLM, MiniMax, LM Studio) or
/// <c>anthropic</c>. With a server of one's own, <c>key</c> may be empty.
/// </para>
/// </remarks>
public sealed record AiAccessV1(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("key")] string Key);
