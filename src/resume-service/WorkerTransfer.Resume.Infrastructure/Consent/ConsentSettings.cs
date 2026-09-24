namespace WorkerTransfer.Resume.Infrastructure.Consent;

/// <summary>Where the consent ledger answers.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Consent__BaseUrl</c>,
/// <c>Consent__Timeout</c>); renaming one alone leaves its value on the default,
/// silently.
/// </remarks>
public sealed class ConsentSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// How long one question may take.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A silent ledger has to become a 503 quickly; hanging
    /// on it would turn one slow dependency into a service that answers
    /// nothing at all.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
