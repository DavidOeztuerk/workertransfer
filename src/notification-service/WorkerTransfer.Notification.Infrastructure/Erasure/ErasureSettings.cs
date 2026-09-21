namespace WorkerTransfer.Notification.Infrastructure.Erasure;

/// <summary>The secret the erasure cascade identifies itself with.</summary>
/// <remarks>
/// Expressly a <em>different</em> one from the notification's: "may trigger a
/// mail" and "may erase everything about a human being" must not be the same
/// piece of paper (ADR-0027 §4.4). Empty means shut.
/// </remarks>
public sealed class ErasureSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Erasure";

    /// <summary>The shared secret. The property name IS the environment key.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
