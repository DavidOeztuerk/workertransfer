namespace WorkerTransfer.Portfolio.Infrastructure.Erasure;

/// <summary>What proves an erasure order really came out of the cascade.</summary>
public sealed class ErasureSettings
{
    /// <summary>The section in the configuration.</summary>
    public const string Section = "Erasure";

    /// <summary>
    /// The shared secret out of <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Empty means: the endpoint is shut, not open.</strong> On a route
    /// that erases a human being, a default that opens in case of doubt would
    /// be the worst possible one — and an unset variable is exactly that case
    /// of doubt.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
