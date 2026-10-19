namespace WorkerTransfer.Profile.Infrastructure.Erasure;

/// <summary>What proves that an erasure command really came from the cascade.</summary>
public sealed class ErasureSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Erasure";

    /// <summary>
    /// The shared secret the origin shows in <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// Expressly a different one from every notification secret: "may trigger a
    /// mail" and "may delete everything about a human being" must not be the
    /// same piece of paper (ADR-0027 §4.4).
    /// <para>
    /// <strong>Empty means: the endpoint is shut, not open.</strong> On a route
    /// that erases a human being, a default that opens in case of doubt would be
    /// the worst one — and an unset variable is exactly the case of doubt.
    /// </para>
    /// <para>
    /// The property name IS the environment key (<c>Erasure__Secret</c>); renaming
    /// it alone leaves the value on its default, silently.
    /// </para>
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
}
