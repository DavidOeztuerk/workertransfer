namespace WorkerTransfer.Applications.Infrastructure.Consent;

/// <summary>Where the consent ledger answers.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Consent__Adresse</c>, <c>Consent__Zeitueberschreitung</c>), so they stay
/// German until the keys are renamed everywhere at once — in compose, in the
/// chart and in <c>.env.example</c>.
/// </remarks>
public sealed class ConsentSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>How long a call may take.</summary>
    /// <remarks>
    /// Short on purpose. A silent ledger must turn into a 503 quickly; hanging
    /// on it would turn a slow dependency into a service that answers nothing at
    /// all.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}
