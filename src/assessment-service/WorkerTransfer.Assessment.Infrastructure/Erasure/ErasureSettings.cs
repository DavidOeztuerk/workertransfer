namespace WorkerTransfer.Assessment.Infrastructure.Erasure;

/// <summary>What proves that an erasure command really came from the cascade.</summary>
public sealed class ErasureSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Erasure";

    /// <summary>The shared secret from <c>X-Erasure-Secret</c>.</summary>
    /// <remarks>
    /// <strong>Empty means the endpoint is shut, not open.</strong> On a route
    /// that erases a human being's work and the feedback on it, a default that
    /// opens in case of doubt would be the worst one — and an unset variable is
    /// exactly the case of doubt.
    /// <para>
    /// The property name IS the environment key (<c>Erasure__Geheimnis</c>), so
    /// it stays German until the keys are renamed everywhere at once — in
    /// compose, in the chart and in <c>.env.example</c>.
    /// </para>
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
