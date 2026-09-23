namespace WorkerTransfer.Transfer.Infrastructure.Erasure;

/// <summary>The secret the erasure cascade identifies itself with.</summary>
/// <remarks>
/// Expressly a <em>different</em> one from the notification's: "may trigger a
/// mail" and "may erase everything about a human being" must not be the same
/// piece of paper (ADR-0027 §4.4). Empty means shut — on an endpoint that
/// erases everything about a human being, a default that opens in case of doubt
/// would be the worst imaginable.
/// </remarks>
public sealed class ErasureSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Erasure";

    /// <summary>The shared secret from <c>X-Erasure-Secret</c>.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Erasure__Geheimnis</c>), so
    /// it stays German until the keys are renamed everywhere at once — in
    /// compose, in the chart and in <c>.env.example</c>.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
