using System.Reflection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>Does the context that leaves for the model carry more than agreed?</summary>
/// <remarks>
/// <para><strong>It survives the migration because Noelia cannot have it.</strong>
/// Which four fields may go out to a model is a statement about <em>this</em>
/// consumer (ADR-0024 §3). A library does not know the class that IS the
/// boundary.</para>
///
/// <para><strong>Against a written-out set, not against a forbidden
/// list.</strong> A forbidden list answers "is this name sensitive?" — a
/// question one answers afresh, and possibly wrongly, at the next field. A
/// closed set demands that whoever adds a field writes it down here: the same
/// movement, but one that stands out.</para>
/// </remarks>
/// <param name="type">The context class that IS the boundary.</param>
/// <param name="erwartet">The agreed field set.</param>
/// <param name="wofuer">What the context is there for, in three words.</param>
public sealed class SeamCheck(Type type, IReadOnlyList<string> erwartet, string wofuer)
    : ISecurityCheck
{
    /// <inheritdoc />
    public string Id => "wt.ki.naht";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Selection rule — see <c>ProviderCheck</c>.</remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;

    /// <inheritdoc />
    public string Remediation =>
        "Entweder das Feld wieder entfernen, oder die vereinbarte Menge im "
        + "Verbundpunkt dieses Dienstes ergänzen — und dabei beantworten, was "
        + "mit diesem Feld über einen Menschen hinausgeht.";

    /// <inheritdoc />
    public IReadOnlyList<RegulatoryReference> References =>
    [
        RegulatoryReferences.GdprRecordsOfProcessing,
        AdditionalRegulatoryReferences.AnnexThree
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(erwartet);

        var found = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(eigenschaft => eigenschaft.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var dazu = found.Except(erwartet, StringComparer.Ordinal).ToList();
        var fehlend = erwartet.Except(found, StringComparer.Ordinal).ToList();

        if (dazu.Count == 0 && fehlend.Count == 0)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Pass,
                $"Der Kontext {wofuer} trägt genau {found.Count} Felder: "
                + $"{string.Join(", ", found)}. Mehr verlässt diesen Dienst "
                + "auf dem Weg zum Modell nicht.",
                "Ein Feld mehr an dieser Klasse schickt etwas Neues über einen "
                + "Menschen hinaus — und stößt diesen Befund um."));
        }

        return Task.FromResult(Ergebnis(
            SecurityCheckStatus.Fail,
            $"Der Kontext {wofuer} weicht von der vereinbarten Feldmenge ab: "
            + (dazu.Count > 0 ? $"zusätzlich {string.Join(", ", dazu)}" : "")
            + (dazu.Count > 0 && fehlend.Count > 0 ? "; " : "")
            + (fehlend.Count > 0 ? $"es fehlen {string.Join(", ", fehlend)}" : "")
            + ".",
            Remediation));
    }

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus state, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, state, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
