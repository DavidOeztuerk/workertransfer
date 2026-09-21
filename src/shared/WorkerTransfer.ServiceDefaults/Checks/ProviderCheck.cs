using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;
using Noelia.Abstractions.Sovereignty;
using Noelia.Infrastructure.Sovereignty;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>Who set the provider up.</summary>
public enum ProviderOrigin
{
    /// <summary>The operator, in the environment — one access that applies to everybody.</summary>
    Betreiber,

    /// <summary>The person, in their account settings — one access per human being.</summary>
    Person
}

/// <summary>A provider as it is really in use in this instance.</summary>
/// <remarks>
/// <strong><see cref="Menschen"/> counts and does not name.</strong> "Three
/// people on <c>api.anthropic.com</c>" is a statement about the instance; "this
/// person on that server" would be one about a human being and does not belong
/// in a document somebody passes around (ADR-0026). With
/// <see cref="ProviderOrigin.Betreiber"/> this stands at <c>0</c>: the access
/// hangs on no person.
/// </remarks>
/// <param name="Etikett">The provider label: <c>anthropic</c>, <c>openai_compatible</c>.</param>
/// <param name="Host">The host of its address — never the whole address, never a key.</param>
/// <param name="ProviderOrigin">Who set it up.</param>
/// <param name="Menschen">How many people entered it. With the operator: 0.</param>
public sealed record ProviderRow(
    string Etikett,
    string Host,
    ProviderOrigin ProviderOrigin,
    int Menschen);

/// <summary>Where the evidence learns which providers are in use.</summary>
/// <remarks>
/// A port, because the answer stands elsewhere for each service: in
/// identity-service in a table, in profile-service and jobs-service in the
/// environment.
/// </remarks>
public interface IProviderSource
{
    /// <summary>What is entered in this instance, aggregated.</summary>
    /// <param name="ct">Cancels when the caller hangs up.</param>
    /// <returns>The rows. Empty means nothing is set up here.</returns>
    Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default);
}

/// <summary>The provider the operator set up in the environment.</summary>
/// <remarks>
/// <strong>Not set up means: no row.</strong> A row "anthropic, not in use"
/// would be an entry about a recipient that does not exist — and the default
/// value of the address would then stand in a document as though somebody were
/// speaking to it.
/// </remarks>
/// <param name="label">The provider label.</param>
/// <param name="address">The address out of the configuration.</param>
/// <param name="eingerichtet">Whether anybody is called at all.</param>
public sealed class OperatorProviderSource(string label, string address, bool eingerichtet)
    : IProviderSource
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default) =>
        !eingerichtet
        || !Uri.TryCreate(address, UriKind.Absolute, out var target)
        || target.Host.Length == 0
            ? Task.FromResult<IReadOnlyList<ProviderRow>>([])
            : Task.FromResult<IReadOnlyList<ProviderRow>>(
                [new ProviderRow(label, target.Host, ProviderOrigin.Betreiber, 0)]);
}

/// <summary>Which AI providers are actually in use in this instance?</summary>
/// <remarks>
/// <para><strong>It survives the migration, and the reason is measured.</strong>
/// Noelia's <c>noelia.ai.inventory</c> reads the <em>declared</em> dependencies
/// and recognises a model endpoint by its host name
/// (<c>HostJurisdiction.IsArtificialIntelligence</c>). Declaring happens at
/// <em>startup</em> — and that is exactly where the access does not stand here:
/// it stands in <c>KiZugangV1</c>, per person, in a table. Noelia's directory
/// is therefore rightly a lower bound; this one here can be complete.</para>
///
/// <para><strong>And exactly for that reason the finding is about
/// people.</strong> It names provider and host and <em>counts</em>. Who uses
/// which provider stands nowhere — neither here nor on the page nor in
/// <c>report.json</c>.</para>
///
/// <para><strong>The jurisdiction comes from Noelia, not from a rebuild.</strong>
/// <c>HostJurisdiction.Classify</c> distinguishes self-operated destinations
/// from ones in a third country; our earlier <c>Zielkunde</c> did the same
/// thing worse and has fallen. Two classifications of the same question are two
/// truths.</para>
/// </remarks>
/// <param name="quellen">Where the rows come from. None means there is no AI here.</param>
public sealed class ProviderCheck(IEnumerable<IProviderSource> quellen) : ISecurityCheck
{
    /// <inheritdoc />
    public string Id => "wt.ki.anbieter";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>
    /// <c>Composition</c> is a selection rule, not cosmetics: Noelia's runner
    /// takes a check only with this category or with a composed module.
    /// Choosing a module that happens to be composed would mean the check
    /// silently disappears as soon as somebody deselects it.
    /// </remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.Medium;

    /// <inheritdoc />
    public string Remediation =>
        "Jeder Anbieter außerhalb des eigenen Netzes ist ein Empfänger im Sinne "
        + "von Art. 30 Abs. 1 DSGVO. In den Aktenschrank gehören: der "
        + "Auftragsverarbeitungsvertrag, das Land der Verarbeitung und — "
        + "außerhalb der Union — die Garantie.";

    /// <inheritdoc />
    public IReadOnlyList<RegulatoryReference> References =>
    [
        RegulatoryReferences.GdprRecordsOfProcessing,
        RegulatoryReferences.GdprProcessorContract,
        RegulatoryReferences.GdprThirdCountryTransfer,
        RegulatoryReferences.AiActTransparency
    ];

    /// <inheritdoc />
    public async Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quellen);

        var rows = new List<ProviderRow>();

        foreach (var source in quellen)
        {
            rows.AddRange(await source.ReadAsync(cancellationToken));
        }

        var gebuendelt = rows
            .GroupBy(row => (row.Etikett, row.Host, row.ProviderOrigin))
            .Select(group => new ProviderRow(
                group.Key.Etikett,
                group.Key.Host,
                group.Key.ProviderOrigin,
                group.Sum(row => row.Menschen)))
            .OrderBy(row => row.Etikett, StringComparer.Ordinal)
            .ThenBy(row => row.Host, StringComparer.Ordinal)
            .ToList();

        if (gebuendelt.Count == 0)
        {
            // NotApplicable and not Pass: a green tick on something that does
            // not apply here at all is noise in exactly the document that is
            // meant to cut through noise.
            return Ergebnis(
                SecurityCheckStatus.NotApplicable,
                "In dieser Instanz ist kein KI-Anbieter eingetragen. Ohne "
                + "Eintrag wird kein Modell gefragt, und die Oberfläche sagt "
                + "das, statt eine Vorlage auszugeben, die wie ein Vorschlag "
                + "aussieht.",
                "Wer einen einträgt, erscheint ab dann in diesem Verzeichnis — "
                + "als Etikett und Host, gezählt, nie namentlich.");
        }

        var draussen = gebuendelt
            .Where(row => HostJurisdiction.Classify(row.Host).Jurisdiction
                            != Jurisdiction.SelfHosted)
            .ToList();

        return Ergebnis(
            draussen.Count > 0 ? SecurityCheckStatus.Warning : SecurityCheckStatus.Pass,
            string.Join("; ", gebuendelt.Select(Satz)) + ".",
            draussen.Count > 0
                ? Remediation
                : "Solange jeder Eintrag im eigenen Netz liegt, verlässt kein "
                  + "Wort das Haus. Ein Eintrag mit einer öffentlichen Adresse "
                  + "ändert das, und zwar ohne Codeänderung.");
    }

    /// <summary>One row as a human being reads it.</summary>
    private static string Satz(ProviderRow row)
    {
        var lage = HostJurisdiction.Classify(row.Host);

        return row.ProviderOrigin == ProviderOrigin.Betreiber
            ? $"{row.Etikett} auf {row.Host} ({Wort(lage.Jurisdiction)}), vom "
              + "Betreiber eingerichtet und für alle gültig"
            : $"{row.Menschen} Mensch(en) auf {row.Host} über {row.Etikett} "
              + $"({Wort(lage.Jurisdiction)})";
    }

    private static string Wort(Jurisdiction lage) => lage switch
    {
        Jurisdiction.SelfHosted => "eigenes Netz",
        Jurisdiction.ThirdCountryProvider => "Drittland",
        _ => "Rechtsraum offen"
    };

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus state, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, state, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
