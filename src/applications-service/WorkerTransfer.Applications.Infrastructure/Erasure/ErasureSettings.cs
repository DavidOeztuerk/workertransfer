namespace WorkerTransfer.Applications.Infrastructure.Erasure;

/// <summary>The secret with which the erasure cascade identifies itself.</summary>
/// <remarks>
/// Expressly a <em>different</em> one from the notification's: "may trigger a
/// mail" and "may erase everything about a person" must not be the same paper
/// (ADR-0027 §4.4). Empty means shut — for an endpoint that erases everything
/// about a person, a default that opens in doubt would be the worst imaginable.
/// <para>
/// The section and the property name ARE the environment key
/// (<c>Erasure__Geheimnis</c>), so they stay as they are until the keys are
/// renamed everywhere at once — in compose, in the chart and in
/// <c>.env.example</c>.
/// </para>
/// </remarks>
public sealed class ErasureSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Erasure";

    /// <summary>The shared secret.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}
