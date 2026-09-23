namespace WorkerTransfer.Resume.Infrastructure.Erasure;

/// <summary>What proves an erasure order really came from the cascade.</summary>
public sealed class ErasureSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Erasure";

    /// <summary>
    /// The shared secret the origin presents in <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// Expressly a different one from the notification secret: "may trigger a
    /// mail" and "may delete everything about a person" must not be the same
    /// piece of paper (ADR-0027 §4.4).
    /// <para>
    /// <strong>Empty means the endpoint is shut, not open.</strong> For a route
    /// that erases a person, a default that opens in case of doubt would be the
    /// worst possible one — and an unset variable is exactly the case of doubt.
    /// </para>
    /// <para>
    /// The property name IS the environment key (<c>Erasure__Geheimnis</c>), so
    /// it stays German until the keys are renamed everywhere at once — in
    /// compose, in the chart and in <c>.env.example</c>.
    /// </para>
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
