namespace WorkerTransfer.Resume.Infrastructure.Loeschung;

/// <summary>What proves a deletion order really came from the cascade.</summary>
public sealed class Loescheinstellungen
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Abschnitt = "Erasure";

    /// <summary>
    /// The shared secret the origin presents in <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// Expressly a different one from the notification secret: "may trigger a
    /// mail" and "may delete everything about a person" must not be the same
    /// piece of paper (ADR-0027 §4.4).
    /// <para>
    /// <strong>Empty means the endpoint is shut, not open.</strong> For a route
    /// that erases a person, a default that opens in case of doubt would be the
    /// worst possible one — and an unset variable is exactly the case of doubt.
    /// </para>
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
