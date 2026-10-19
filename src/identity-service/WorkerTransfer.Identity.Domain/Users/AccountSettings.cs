using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Which provider a person's own key is for (ADR-0051).</summary>
/// <remarks>
/// <para><strong><see cref="None"/> means the platform's model</strong>, not
/// "off": whoever sets nothing writes with what the platform runs — until the
/// start the operator's local Ollama. A key of one's own replaces it for
/// everything the person drafts.</para>
///
/// <para><strong>Two cloud providers for now, more later</strong> (Gemini,
/// Ollama Cloud, Hugging Face). The address belongs to the provider and is set
/// by the platform, never typed by the person: a free address would be a way
/// to send a person's texts to any server, and the egress boundary only knows
/// the hosts the operator declared.</para>
/// </remarks>
public enum AiProvider
{
    /// <summary>No own key — the platform's model.</summary>
    None,

    /// <summary>OpenAI, with the person's own key.</summary>
    OpenAi,

    /// <summary>Anthropic, with the person's own key.</summary>
    Anthropic
}

/// <summary>What each provider's address and default model are.</summary>
public static class AiProviders
{
    /// <summary>The endpoint the platform uses for a provider; empty for <see cref="AiProvider.None"/>.</summary>
    public static string Address(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => "https://api.openai.com/v1/chat/completions",
        AiProvider.Anthropic => "https://api.anthropic.com/v1/messages",
        _ => string.Empty
    };

    /// <summary>The model taken when the person names none — empty where we name none.</summary>
    /// <remarks>
    /// Anthropic's current default model. For OpenAI the person names the model
    /// from her own account; a guessed name would fail at the first call.
    /// </remarks>
    public static string DefaultModel(AiProvider provider) =>
        provider == AiProvider.Anthropic ? "claude-opus-5-5" : string.Empty;
}

/// <summary>
/// What a person decided about themselves and about the processing of their data.
/// </summary>
/// <remarks>
/// <strong>Everything here is a decision, not a derivation.</strong> No value is
/// concluded from behaviour, none from belonging to a company. What was not set
/// stands at the most reticent setting — not at the most convenient one.
/// <para>
/// <strong>The key lies encrypted and never comes back.</strong> The interface
/// learns WHETHER one is stored and what its last four characters are; nobody
/// needs more to recognise it again, and more must not travel back over the
/// wire. A secret you can retrieve is one you can siphon off.
/// </para>
/// </remarks>
public sealed class AccountSettings
{
    private AccountSettings(SubjectId subject)
    {
        Subject = subject;
    }

    /// <summary>Whose settings.</summary>
    public SubjectId Subject { get; }

    // ----------------------------------------------------------------- Data

    /// <summary>
    /// After how many months without a sign-in the account deletes itself.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means "never by itself". That is the default, because an
    /// automatic deletion is a decision about somebody else's data that nobody
    /// took — and because an account that lapses while you are on holiday is
    /// the opposite of control.
    /// </remarks>
    public int? DeleteAfterMonths { get; private set; }

    // ------------------------------------------------------------------- AI

    /// <summary>Which provider may be asked.</summary>
    public AiProvider Provider { get; private set; } = AiProvider.None;

    /// <summary>The provider's address — set by the platform, never by the person.</summary>
    public string Address => AiProviders.Address(Provider);

    /// <summary>Which model.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>The key, encrypted. Empty means: none stored.</summary>
    /// <remarks>
    /// Never leaves this service in the clear and never goes back to the
    /// browser.
    /// </remarks>
    public string EncryptedKey { get; private set; } = string.Empty;

    /// <summary>The last four characters, for recognising it again.</summary>
    public string KeyTail { get; private set; } = string.Empty;

    /// <summary>
    /// Whether it is recorded THAT a request went out — never what was in it.
    /// </summary>
    /// <remarks>
    /// The EU AI Act demands of operators of a system in an employment context
    /// a log of the system's activity. This platform evaluates nobody and thus
    /// does not fall under the duties for high-risk systems (ADR-0022,
    /// ADR-0024) — whoever wants the log anyway gets it. It records the time
    /// and the kind, never the text: otherwise the very collection the draft
    /// avoids would come into being while proving it.
    /// </remarks>
    public bool AiAuditLog { get; private set; }

    // ------------------------------------------------------------------ View

    /// <summary>The settings as a row holds them.</summary>
    /// <param name="subject">Whose.</param>
    /// <param name="deleteAfterMonths">The lapse, or <c>null</c>.</param>
    /// <param name="provider">Which AI provider.</param>
    /// <param name="model">Its model.</param>
    /// <param name="encryptedKey">The key, encrypted.</param>
    /// <param name="keyTail">The last four characters.</param>
    /// <param name="aiAuditLog">Whether requests are logged.</param>
    /// <returns>The settings.</returns>
    public static AccountSettings Restore(
        SubjectId subject,
        int? deleteAfterMonths,
        AiProvider provider,
        string model,
        string encryptedKey,
        string keyTail,
        bool aiAuditLog) =>
        new(subject)
        {
            DeleteAfterMonths = deleteAfterMonths,
            Provider = provider,
            Model = model,
            EncryptedKey = encryptedKey,
            KeyTail = keyTail,
            AiAuditLog = aiAuditLog
        };

    /// <summary>The most reticent setting — for an account that never set anything.</summary>
    /// <param name="subject">Whose.</param>
    /// <returns>Settings with no provider, no lapse, no log.</returns>
    public static AccountSettings Default(SubjectId subject) => new(subject);

    /// <summary>Sets the data-protection decisions.</summary>
    /// <param name="deleteAfterMonths">
    /// The lapse in months, or <c>null</c> for "never by itself". Values under
    /// three months are refused: an account that disappears after four weeks is
    /// one somebody loses while on holiday.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The lapse is too short.</exception>
    public void SetPrivacy(int? deleteAfterMonths)
    {
        if (deleteAfterMonths is { } months && months is < 3 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deleteAfterMonths), months,
                "A lapse lies between 3 and 120 months.");
        }

        DeleteAfterMonths = deleteAfterMonths;
    }

    /// <summary>Sets the AI provider. The key is not touched by it.</summary>
    /// <remarks>
    /// Separate from the key, because they are two cases: the provider is
    /// changed often, the key rarely. Setting both in one call would mean
    /// sending the key along every time — and a secret that travels over the
    /// wire at every save travels over the wire more often than it has to.
    /// </remarks>
    /// <param name="provider">Which one.</param>
    /// <param name="model">Its model; empty takes the provider's default.</param>
    /// <param name="aiAuditLog">Whether requests are logged.</param>
    public void SetAi(AiProvider provider, string model, bool aiAuditLog)
    {
        var named = (model ?? string.Empty).Trim();

        Provider = provider;
        Model = provider == AiProvider.None
            ? string.Empty
            : named.Length > 0 ? named : AiProviders.DefaultModel(provider);
        AiAuditLog = aiAuditLog;

        // No provider means no key. Leaving it standing would be a secret with
        // no purpose — and that is the worst kind there is to keep.
        if (provider == AiProvider.None)
        {
            EncryptedKey = string.Empty;
            KeyTail = string.Empty;
        }
    }

    /// <summary>Stores a key.</summary>
    /// <param name="encrypted">The key, already encrypted.</param>
    /// <param name="suffix">Its last four characters.</param>
    public void SetKey(string encrypted, string suffix)
    {
        EncryptedKey = encrypted ?? string.Empty;
        KeyTail = suffix ?? string.Empty;
    }

    /// <summary>Removes the key.</summary>
    public void ClearKey()
    {
        EncryptedKey = string.Empty;
        KeyTail = string.Empty;
    }
}
