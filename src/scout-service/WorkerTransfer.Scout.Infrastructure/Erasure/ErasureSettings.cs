namespace WorkerTransfer.Scout.Infrastructure.Erasure;

/// <summary>What proves that an erasure command really came from the cascade.</summary>
/// <remarks>
/// The section and the property name ARE the environment key
/// (<c>Erasure__Geheimnis</c>), so they stay German until the keys are renamed
/// everywhere at once — in compose, in the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class ErasureSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Erasure";

    /// <summary>
    /// The shared secret from <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Empty means: the endpoint is shut, not open.</strong> For a route
    /// that erases a human being, a default that opens in doubt would be the
    /// worst — and an unset variable is exactly the case of doubt.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
