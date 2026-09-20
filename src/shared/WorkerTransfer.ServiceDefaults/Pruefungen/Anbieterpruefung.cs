using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;
using Noelia.Abstractions.Sovereignty;
using Noelia.Infrastructure.Sovereignty;

namespace WorkerTransfer.ServiceDefaults.Pruefungen;

/// <summary>Wer den Anbieter eingerichtet hat.</summary>
public enum Herkunft
{
    /// <summary>Der Betreiber, in der Umgebung — ein Zugang, der für alle gilt.</summary>
    Betreiber,

    /// <summary>Die Person, in ihren Kontoeinstellungen — ein Zugang je Mensch.</summary>
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
public sealed record Anbieterzeile(
    string Etikett,
    string Host,
    Herkunft Herkunft,
    int Menschen);

/// <summary>Woher der Nachweis erfährt, welche Anbieter in Gebrauch sind.</summary>
/// <remarks>
/// Ein Port, weil die Antwort je Dienst woanders steht: in identity-service in
/// einer Tabelle, in profile-service und jobs-service in der Umgebung.
/// </remarks>
public interface IAnbieterquelle
{
    /// <summary>Was in dieser Instanz eingetragen ist, aggregiert.</summary>
    /// <param name="ct">Bricht ab, wenn der Aufrufer auflegt.</param>
    /// <returns>Die Zeilen. Leer heißt: hier ist nichts eingerichtet.</returns>
    Task<IReadOnlyList<Anbieterzeile>> LeseAsync(CancellationToken ct = default);
}

/// <summary>Der Anbieter, den der Betreiber in der Umgebung eingerichtet hat.</summary>
/// <remarks>
/// <strong>Nicht eingerichtet heißt: keine Zeile.</strong> Eine Zeile
/// „anthropic, nicht in Gebrauch" wäre ein Eintrag über einen Empfänger, den es
/// nicht gibt — und der Vorgabewert der Adresse stünde dann in einem Dokument,
/// als spräche jemand mit ihm.
/// </remarks>
/// <param name="etikett">Das Anbieter-Etikett.</param>
/// <param name="adresse">Die Adresse aus der Konfiguration.</param>
/// <param name="eingerichtet">Ob überhaupt jemand gerufen wird.</param>
public sealed class Betreiberquelle(string etikett, string adresse, bool eingerichtet)
    : IAnbieterquelle
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Anbieterzeile>> LeseAsync(CancellationToken ct = default) =>
        !eingerichtet
        || !Uri.TryCreate(adresse, UriKind.Absolute, out var ziel)
        || ziel.Host.Length == 0
            ? Task.FromResult<IReadOnlyList<Anbieterzeile>>([])
            : Task.FromResult<IReadOnlyList<Anbieterzeile>>(
                [new Anbieterzeile(etikett, ziel.Host, Herkunft.Betreiber, 0)]);
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
/// <param name="quellen">Woher die Zeilen kommen. Keine heißt: hier gibt es keine KI.</param>
public sealed class Anbieterpruefung(IEnumerable<IAnbieterquelle> quellen) : ISecurityCheck
{
    /// <inheritdoc />
    public string Id => "wt.ki.anbieter";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>
    /// <strong><c>Composition</c> und nicht die inhaltlich passende Kategorie —
    /// das ist eine AUSWAHLREGEL, keine Kosmetik.</strong> Noelias Läufer nimmt
    /// eine Prüfung nur, wenn ihre Kategorie <c>Composition</c> ist ODER ihr
    /// Modul wirklich komponiert wurde. Diese Prüfung hängt an keinem Modul: sie
    /// gilt unabhängig davon, was dieser Dienst komponiert.
    /// <para>
    /// Gemessen: mit einer inhaltlichen Kategorie stand sie in keinem Bericht —
    /// dreizehn Prüfungen liefen, unsere sechs nicht, und nichts sagte es. Ein
    /// Modul zu wählen, das zufällig komponiert ist, wäre die schlechtere
    /// Antwort: die Prüfung verschwände still, sobald jemand dieses Modul
    /// abwählt.
    /// </para>
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

        var zeilen = new List<Anbieterzeile>();

        foreach (var quelle in quellen)
        {
            zeilen.AddRange(await quelle.LeseAsync(cancellationToken));
        }

        var gebuendelt = zeilen
            .GroupBy(zeile => (zeile.Etikett, zeile.Host, zeile.Herkunft))
            .Select(gruppe => new Anbieterzeile(
                gruppe.Key.Etikett,
                gruppe.Key.Host,
                gruppe.Key.Herkunft,
                gruppe.Sum(zeile => zeile.Menschen)))
            .OrderBy(zeile => zeile.Etikett, StringComparer.Ordinal)
            .ThenBy(zeile => zeile.Host, StringComparer.Ordinal)
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
            .Where(zeile => HostJurisdiction.Classify(zeile.Host).Jurisdiction
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
    private static string Satz(Anbieterzeile zeile)
    {
        var lage = HostJurisdiction.Classify(zeile.Host);

        return zeile.Herkunft == Herkunft.Betreiber
            ? $"{zeile.Etikett} auf {zeile.Host} ({Wort(lage.Jurisdiction)}), vom "
              + "Betreiber eingerichtet und für alle gültig"
            : $"{zeile.Menschen} Mensch(en) auf {zeile.Host} über {zeile.Etikett} "
              + $"({Wort(lage.Jurisdiction)})";
    }

    private static string Wort(Jurisdiction lage) => lage switch
    {
        Jurisdiction.SelfHosted => "eigenes Netz",
        Jurisdiction.ThirdCountryProvider => "Drittland",
        _ => "Rechtsraum offen"
    };

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus stand, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, stand, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
