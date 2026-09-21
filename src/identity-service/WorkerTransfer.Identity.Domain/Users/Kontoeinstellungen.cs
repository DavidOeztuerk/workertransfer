using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Which provider is asked for the drafting help.</summary>
/// <remarks>
/// <strong>Two values for many providers.</strong> Ollama, OpenAI, vLLM,
/// LM Studio and Mistral all speak the same shape
/// (<c>/v1/chat/completions</c>); Anthropic speaks one of its own. So what
/// stands here is not a list of company names but the question that
/// distinguishes the code — a list of companies would be a migration at every
/// new provider.
/// <para>
/// <strong><see cref="None"/> is the default and no embarrassment.</strong>
/// Whoever sets nothing asks nobody, and the interface says so. Defaulting to
/// an outside provider would be a consent nobody gave.
/// </para>
/// </remarks>
public enum AiProvider
{
    /// <summary>None. Nothing is given outwards.</summary>
    None,

    /// <summary>Everything that speaks <c>/v1/chat/completions</c> — Ollama included.</summary>
    OpenAiCompatible,

    /// <summary>Anthropic's own shape.</summary>
    Anthropic
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
    /// <c>null</c> means „never by itself". That is the default, because an
    /// automatic deletion is a decision about somebody else's data that nobody
    /// took — and because an account that lapses while you are on holiday is
    /// the opposite of control.
    /// </remarks>
    public int? DeleteAfterMonths { get; private set; }

    // ------------------------------------------------------------------- AI

    /// <summary>Which provider may be asked.</summary>
    public AiProvider Provider { get; private set; } = AiProvider.None;

    /// <summary>The provider's address. With Ollama, one's own machine.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>Which model.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>The key, encrypted. Empty means: none stored.</summary>
    /// <remarks>
    /// Never leaves this service in the clear and never goes back to the
    /// browser. With a local Ollama it stays empty — there is none there.
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
    /// <param name="address">Its address.</param>
    /// <param name="model">Its model.</param>
    /// <param name="encryptedKey">The key, encrypted.</param>
    /// <param name="keyTail">The last four characters.</param>
    /// <param name="aiAuditLog">Whether requests are logged.</param>
    /// <returns>The settings.</returns>
    public static AccountSettings Restore(
        SubjectId subject,
        int? deleteAfterMonths,
        AiProvider provider,
        string address,
        string model,
        string encryptedKey,
        string keyTail,
        bool aiAuditLog) =>
        new(subject)
        {
            DeleteAfterMonths = deleteAfterMonths,
            Provider = provider,
            Address = address,
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
    /// The lapse in months, or <c>null</c> for „never by itself". Values under
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
    /// <param name="address">Its address.</param>
    /// <param name="model">Its model.</param>
    /// <param name="aiAuditLog">Whether requests are logged.</param>
    public void SetAi(AiProvider provider, string address, string model, bool aiAuditLog)
    {
        Provider = provider;
        Address = (address ?? string.Empty).Trim();
        Model = (model ?? string.Empty).Trim();
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
