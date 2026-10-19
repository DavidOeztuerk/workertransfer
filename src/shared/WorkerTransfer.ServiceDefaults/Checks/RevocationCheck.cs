using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>Does anything else stand between the question and the ledger?</summary>
/// <remarks>
/// <para><strong>It survives the migration because Noelia does not know the
/// ledger.</strong> <c>noelia.revocation.*</c> measures Noelia's own revocation
/// list for tokens; this product's consent ledger is a service with a promise
/// of its own (ADR-0013): a revocation has to take effect on the NEXT
/// read.</para>
///
/// <para><strong>What is measured, and what is not.</strong> What is measured
/// is which type is resolved when this service uses the consent gate — and
/// whether that is the same one that speaks directly to the ledger. In this
/// system a cache does not arrive as configuration but as one more type in
/// between. That is exactly what this sees.</para>
///
/// <para>It does <em>not</em> claim to have tried a revocation. It could only
/// do so by writing into the ledger, and an operations report that grants and
/// revokes consents in order to evidence itself would be the worst trade in
/// this tree.</para>
/// </remarks>
/// <typeparam name="TGate">The port through which this service asks the ledger.</typeparam>
/// <param name="provider">The container — it is resolved in a scope of its own.</param>
/// <param name="direct">The adapter that really speaks to the ledger.</param>
public sealed class RevocationCheck<TGate>(IServiceProvider provider, Type direct)
    : ISecurityCheck
    where TGate : notnull
{
    /// <inheritdoc />
    public string Id => "wt.einwilligung.wirkt";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Selection rule — see <c>ProviderCheck</c>.</remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;

    /// <inheritdoc />
    public string Remediation =>
        "ADR-0013: ein Widerruf muss beim nächsten Lesen wirken. Solange ein "
        + "anderer Typ dazwischensteht, ist ungeklärt, ob er die Antwort "
        + "aufhebt — und ein Cache an dieser Stelle ist kein Leistungsdetail, "
        + "sondern ein Regelbruch, den man nicht bemerkt.";

    /// <inheritdoc />
    public IReadOnlyList<RegulatoryReference> References =>
    [
        AdditionalRegulatoryReferences.Revocation,
        AdditionalRegulatoryReferences.Accountability
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(direct);

        // A scope of its own, because the gate lives per request — and because
        // this check is registered as a singleton. Resolving out of the root
        // provider would throw for a scoped service, and that would look like a
        // finding while being a defect of this check.
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService<TGate>();

        if (resolved is null)
        {
            return Task.FromResult(Finding(
                SecurityCheckStatus.NotApplicable,
                $"Dieser Dienst fragt den Ledger nicht: für {ShortName(typeof(TGate))} "
                + "ist nichts registriert.",
                "Wer hier eine Sichtbarkeit entscheidet, fragt den Ledger — und "
                + "zwar synchron und ohne Zwischenspeicher (ADR-0013)."));
        }

        var actual = resolved.GetType();

        return actual == direct
            ? Task.FromResult(Finding(
                SecurityCheckStatus.Pass,
                $"Zwischen der Frage ({ShortName(typeof(TGate))}) und dem Ledger steht "
                + $"kein weiterer Typ: aufgelöst wird {ShortName(direct)}, der "
                + "Adapter, der unmittelbar fragt. Kein Zwischenspeicher, kein "
                + "Umhüller.",
                "Ein Umhüller an dieser Stelle — auch einer, der nur für eine "
                + "Minute merkt — stößt diesen Befund um. Ein Widerruf wirkte "
                + "dann beim übernächsten Lesen, und niemand sähe es."))
            : Task.FromResult(Finding(
                SecurityCheckStatus.Fail,
                $"Zwischen der Frage ({ShortName(typeof(TGate))}) und dem Ledger steht "
                + $"{ShortName(actual)} statt {ShortName(direct)}.",
                Remediation));
    }

    /// <summary>A type name without a namespace — a shape, not a value.</summary>
    private static string ShortName(Type type) => type.Name;

    private SecurityCheckResult Finding(
        SecurityCheckStatus state, string summary, string remediation) =>
        new(Id, Module, Category, state, Severity, summary, remediation)
        {
            References = References
        };
}
