using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;
using Noelia.Abstractions.Sovereignty;
using Noelia.Infrastructure.Sovereignty;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>Wer den Anbieter eingerichtet hat.</summary>
public enum ProviderOrigin
{
    /// <summary>Der Betreiber, in der EnvironmentFile — ein Zugang, der für alle gilt.</summary>
    Betreiber,

    /// <summary>Die Person, in ihren AccountSettings — ein Zugang je Mensch.</summary>
    Person
}

/// <summary>Ein Anbieter, wie er in dieser Instanz wirklich in Gebrauch ist.</summary>
/// <remarks>
/// <strong><see cref="Menschen"/> zählt und nennt nicht.</strong> „Drei Menschen
/// auf <c>api.anthropic.com</c>" ist eine Aussage über die Instanz; „diese
/// Person auf jenem Server" wäre eine über einen Menschen und gehört nicht in
/// ein Dokument, das jemand herumreicht (ADR-0026). Bei
/// <see cref="Herkunft.Betreiber"/> steht hier <c>0</c>: der Zugang hängt an
/// keiner Person.
/// </remarks>
/// <param name="Etikett">Das Anbieter-Etikett: <c>anthropic</c>, <c>openai_compatible</c>.</param>
/// <param name="Host">Der Host seiner Adresse — nie die ganze Adresse, nie ein Schlüssel.</param>
/// <param name="Herkunft">Wer ihn eingerichtet hat.</param>
/// <param name="Menschen">Wie viele Menschen ihn eingetragen haben. Bei Betreiber: 0.</param>
public sealed record ProviderRow(
    string Etikett,
    string Host,
    ProviderOrigin ProviderOrigin,
    int Menschen);

/// <summary>Woher der Nachweis erfährt, welche Anbieter in Gebrauch sind.</summary>
/// <remarks>
/// Ein Port, weil die Antwort je Dienst woanders steht: in identity-service in
/// einer Tabelle, in profile-service und jobs-service in der EnvironmentFile.
/// </remarks>
public interface IProviderSource
{
    /// <summary>Was in dieser Instanz eingetragen ist, aggregiert.</summary>
    /// <param name="ct">Bricht ab, wenn der Aufrufer auflegt.</param>
    /// <returns>Die Zeilen. Leer heißt: hier ist nichts eingerichtet.</returns>
    Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default);
}

/// <summary>Der Anbieter, den der Betreiber in der EnvironmentFile eingerichtet hat.</summary>
/// <remarks>
/// <strong>Nicht eingerichtet heißt: keine Zeile.</strong> Eine Zeile
/// „anthropic, nicht in Gebrauch" wäre ein Eintrag über einen Empfänger, den es
/// nicht gibt — und der Vorgabewert der Adresse stünde dann in einem Dokument,
/// als spräche jemand mit ihm.
/// </remarks>
/// <param name="etikett">Das Anbieter-Etikett.</param>
/// <param name="adresse">Die Adresse aus der Konfiguration.</param>
/// <param name="eingerichtet">Ob überhaupt jemand gerufen wird.</param>
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

/// <summary>Welche KI-Anbieter sind in dieser Instanz tatsächlich in Gebrauch?</summary>
/// <remarks>
/// <para><strong>Sie überlebt den Umstieg, und der Grund ist gemessen.</strong>
/// Noelias <c>noelia.ai.inventory</c> liest die <em>deklarierten</em>
/// Abhängigkeiten und erkennt einen Modell-Endpunkt an seinem Hostnamen
/// (<c>HostJurisdiction.IsArtificialIntelligence</c>). Deklariert wird zur
/// <em>Startzeit</em> — und genau dort steht der Zugang hier nicht: er steht in
/// <c>KiZugangV1</c>, je Person, in einer Tabelle. Noelias Verzeichnis ist
/// deshalb zu Recht eine Untergrenze; dieses hier kann vollständig sein.</para>
///
/// <para><strong>Und genau deshalb ist der Befund personenbezogen.</strong> Er
/// nennt Anbieter und Host und <em>zählt</em>. Wer welchen Anbieter benutzt,
/// steht nirgends — weder hier noch auf der Seite noch in
/// <c>report.json</c>.</para>
///
/// <para><strong>Die Lage kommt von Noelia, nicht aus einem Nachbau.</strong>
/// <c>HostJurisdiction.Classify</c> unterscheidet selbst betriebene Ziele von
/// solchen in einem Drittland; unser früherer <c>Zielkunde</c> tat dasselbe
/// schlechter und ist gefallen. Zwei Einteilungen derselben Frage sind zwei
/// Wahrheiten.</para>
/// </remarks>
/// <param name="quellen">Woher die Zeilen kommen. None heißt: hier gibt es keine KI.</param>
public sealed class ProviderCheck(IEnumerable<IProviderSource> quellen) : ISecurityCheck
{
    /// <inheritdoc />
    public string Id => "wt.ki.anbieter";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>
    /// <c>Composition</c> ist eine Auswahlregel, keine Kosmetik: Noelias Läufer
    /// nimmt eine Prüfung nur bei dieser Kategorie oder einem komponierten
    /// Modul. Ein zufällig komponiertes Modul zu wählen hiesse, dass die
    /// Prüfung still verschwindet, sobald jemand es abwählt.
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
            // NotApplicable und nicht Pass: ein gruener Haken an etwas, das hier
            // gar nicht gilt, ist Rauschen in genau dem Dokument, das Rauschen
            // durchschneiden soll.
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

    /// <summary>Eine Zeile, wie ein Mensch sie liest.</summary>
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
