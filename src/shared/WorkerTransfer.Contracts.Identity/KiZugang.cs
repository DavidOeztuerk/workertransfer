using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>
/// Der KI-Zugang einer Person, wie er intern von identity-service zu den
/// Diensten reist, die auf Bitte ein Modell fragen.
/// </summary>
/// <remarks>
/// <strong>Nur über den internen Draht, nie durchs Gateway.</strong> Der
/// Schlüssel steht hier im Klartext, weil der Empfänger ihn an den Anbieter
/// schicken muss — und genau deshalb darf diese Gestalt den Browser nie
/// erreichen. <c>GET /account/settings</c> trägt ihn absichtlich nicht.
/// <para>
/// <c>provider</c> ist das Etikett aus den Kontoeinstellungen:
/// <c>none</c>, <c>openai_compatible</c> (Ollama, vLLM, MiniMax, LM Studio)
/// oder <c>anthropic</c>. Bei einem eigenen Server darf <c>key</c> leer sein.
/// </para>
/// </remarks>
public sealed record KiZugangV1(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("key")] string Key);
