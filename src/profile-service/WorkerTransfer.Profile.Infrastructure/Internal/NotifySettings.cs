namespace WorkerTransfer.Profile.Infrastructure.Internal;

/// <summary>What proves that an internal request really came from a service.</summary>
/// <remarks>
/// The same shared secret as at every other internal door
/// (<c>X-Notify-Secret</c>), and expressly <em>not</em> the erasure one: "may
/// search profiles" and "may delete everything about a human being" must not be
/// the same piece of paper.
/// <para>
/// <strong>Empty means: the door is shut, not open.</strong> An unset variable
/// is the case of doubt, and on a route that hands out profiles a default that
/// opens in case of doubt would be the worst one.
/// </para>
/// </remarks>
public sealed class NotifySettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Notify";

    /// <summary>The shared secret.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Notify__Secret</c>); renaming
    /// it alone leaves the value on its default, silently.
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
}
