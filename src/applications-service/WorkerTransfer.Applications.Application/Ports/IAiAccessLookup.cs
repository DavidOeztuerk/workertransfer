using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>The AI access of the person who asks for the draft.</summary>
/// <remarks>
/// Comes from the account settings (identity-service), not from an environment
/// variable of the platform. The interface sets up Ollama or MiniMax; without
/// this lookup the cover-letter service would only ever see
/// <c>Draft__Schluessel</c> and answer 503 while the person believes she has
/// already asked someone.
/// </remarks>
/// <param name="Provider"><c>none</c>, <c>openai_compatible</c> or <c>anthropic</c>.</param>
/// <param name="Address">The full URL of the endpoint.</param>
/// <param name="Model">Which model.</param>
/// <param name="Key">Plain text. Often empty for a server of one's own.</param>
public sealed record AiAccess(
    string Provider, string Address, string Model, string Key)
{
    /// <summary>Whether a model can be asked with these details.</summary>
    public bool IsSetUp =>
        Address.Length > 0
        && Model.Length > 0
        && Provider switch
        {
            "openai_compatible" => true,
            "anthropic" => Key.Length > 0,
            _ => false
        };
}

/// <summary>Fetches the person's AI access.</summary>
public interface IAiAccessLookup
{
    /// <summary>What this person has set up — or nothing.</summary>
    Task<AiAccess> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
