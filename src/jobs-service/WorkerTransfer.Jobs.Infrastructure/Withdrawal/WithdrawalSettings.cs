namespace WorkerTransfer.Jobs.Infrastructure.Withdrawal;

/// <summary>What proves that a withdrawal command really came from the cascade.</summary>
/// <remarks>
/// The same secret as the erasure, because it is the same origin — but
/// expressly <b>no erasure endpoint</b>: this service holds nothing about a
/// natural person and is therefore no recipient of the cascade (ADR-0027 §2).
/// What arrives here is an intent about a <em>company</em>.
/// <para>
/// The section and the property name ARE the environment key
/// (<c>Erasure__Geheimnis</c>), so they stay as they are until the keys are
/// renamed everywhere at once — in compose, in the chart and in
/// <c>.env.example</c>.
/// </para>
/// </remarks>
public sealed class WithdrawalSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Erasure";

    /// <summary>
    /// The shared secret from <c>X-Erasure-Secret</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Empty means: the endpoint is shut, not open.</strong>
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}
