namespace WorkerTransfer.Applications.Infrastructure.Consent;

/// <summary>Where the consent ledger answers.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Consent__BaseUrl</c>, <c>Consent__Timeout</c>); renaming one alone leaves
/// its value on the default, silently.
/// </remarks>
public sealed class ConsentSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long a call may take.</summary>
    /// <remarks>
    /// Short on purpose. A silent ledger must turn into a 503 quickly; hanging
    /// on it would turn a slow dependency into a service that answers nothing at
    /// all.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
