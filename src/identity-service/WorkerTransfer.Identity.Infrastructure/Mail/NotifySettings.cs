namespace WorkerTransfer.Identity.Infrastructure.Mail;

/// <summary>The secret notification-service identifies itself with.</summary>
/// <remarks>
/// Expressly a <em>different</em> one from the erasure's: "may trigger a mail"
/// and "may delete everything about a human being" must not be the same piece
/// of paper (ADR-0027 §4.4). Empty means shut.
/// </remarks>
public sealed class NotifySettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Notify";

    /// <summary>The shared secret.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Notify__Secret</c>); renaming
    /// it alone leaves the value on its default, silently.
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
}
