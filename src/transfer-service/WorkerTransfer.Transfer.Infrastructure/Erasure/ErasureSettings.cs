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
    /// The property name IS the environment key (<c>Erasure__Secret</c>); renaming
    /// it alone leaves the value on its default, silently.
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
}
