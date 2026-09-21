namespace WorkerTransfer.Identity.Infrastructure.Erasure;

/// <summary>Where the deletion orders go, and what proves they are ours.</summary>
public sealed class ErasureSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Erasure";

    /// <summary>
    /// The shared secret for the cascade.
    /// </summary>
    /// <remarks>
    /// Expressly a different one from the notification secret: "may trigger a
    /// mail" and "may delete everything about a person" must not be the same
    /// piece of paper. Empty means nothing is delivered — and as a failure, not
    /// as a silent skip.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>One base address per recipient, keyed by the name in the kind.</summary>
    public Dictionary<string, string> Adressen { get; } = new(StringComparer.Ordinal);

    /// <summary>How long a recipient is waited for.</summary>
    /// <remarks>
    /// Five seconds. Without this line <c>HttpClient</c>'s default of a HUNDRED
    /// seconds holds — with eight recipients that is over thirteen minutes for
    /// one pass in which one of them hangs.
    /// <para>
    /// Being short costs nothing here: a timeout is a failure, and a failure
    /// leaves the outbox row standing. It is retried, with no attempt ceiling
    /// (ADR-0027). What must NOT happen is the opposite — that a hanging
    /// recipient counts as delivered.
    /// </para>
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}
