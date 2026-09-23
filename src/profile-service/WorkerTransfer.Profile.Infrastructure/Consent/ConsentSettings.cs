namespace WorkerTransfer.Profile.Infrastructure.Consent;

/// <summary>Where the consent ledger can be reached.</summary>
public sealed class ConsentSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    /// <remarks>
    /// In the compose network the service name, not <c>localhost</c> — the call
    /// runs from container to container.
    /// <para>
    /// The property name IS the environment key (<c>Consent__Adresse</c>), so it
    /// stays German until the keys are renamed everywhere at once — in compose,
    /// in the chart and in <c>.env.example</c>.
    /// </para>
    /// </remarks>
    public string Adresse { get; set; } = "http://consent-service:8002";

    /// <summary>
    /// How long to wait for an answer.
    /// </summary>
    /// <remarks>
    /// Short, and that is deliberate: a missing answer becomes 503, and a 503
    /// after five seconds is information, a 503 after sixty a hanging
    /// interface.
    /// <para>
    /// The property name IS the environment key (<c>Consent__Geduld</c>), so it
    /// stays German until the keys are renamed everywhere at once — in compose,
    /// in the chart and in <c>.env.example</c>.
    /// </para>
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}
