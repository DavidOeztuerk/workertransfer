namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>The secret notification-service identifies itself with.</summary>
/// <remarks>
/// Expressly a <em>different</em> one from the erasure's: „may trigger a mail"
/// and „may delete everything about a human being" must not be the same piece
/// of paper (ADR-0027 §4.4). Empty means shut.
/// </remarks>
public sealed class NotifySettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Notify";

    /// <summary>The shared secret.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
