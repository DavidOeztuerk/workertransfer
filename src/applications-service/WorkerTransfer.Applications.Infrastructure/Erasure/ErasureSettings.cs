namespace WorkerTransfer.Applications.Infrastructure.Erasure;

/// <summary>The secret with which the erasure cascade identifies itself.</summary>
/// <remarks>
/// Expressly a <em>different</em> one from the notification's: "may trigger a
/// mail" and "may erase everything about a person" must not be the same paper
/// (ADR-0027 §4.4). Empty means shut — for an endpoint that erases everything
/// about a person, a default that opens in doubt would be the worst imaginable.
/// <para>
/// The section and the property name ARE the environment key
/// (<c>Erasure__Secret</c>); renaming one alone leaves its value on the default,
/// silently.
/// </para>
/// </remarks>
public sealed class ErasureSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Erasure";

    /// <summary>The shared secret.</summary>
    public string Secret { get; set; } = string.Empty;
}
