using System.Reflection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Pruefungen;

/// <summary>Trägt der Kontext, der das Modell verlässt, mehr als vereinbart?</summary>
/// <remarks>
/// <para><strong>Sie überlebt den Umstieg, weil Noelia sie nicht haben kann.</strong>
/// Welche vier Felder zu einem Modell hinausgehen dürfen, ist eine Aussage über
/// <em>diesen</em> Verbraucher (ADR-0024 §3). Eine Bibliothek kennt die Klasse
/// nicht, die die Grenze IST.</para>
///
/// <para><strong>Gegen eine ausgeschriebene Menge, nicht gegen eine
/// Verbotsliste.</strong> Eine Verbotsliste beantwortet „ist dieser Name
/// heikel?" — eine Frage, die man beim nächsten Feld neu und womöglich falsch
/// beantwortet. Eine geschlossene Menge verlangt, dass wer ein Feld hinzufügt,
/// es hier hinschreibt: dieselbe Bewegung, aber eine, die auffällt.</para>
/// </remarks>
/// <param name="typ">Die Kontextklasse, die die Grenze IST.</param>
/// <param name="erwartet">Die vereinbarte Feldmenge.</param>
/// <param name="wofuer">Wofür der Kontext da ist, in drei Worten.</param>
public sealed class Nahtpruefung(Type typ, IReadOnlyList<string> erwartet, string wofuer)
    : ISecurityCheck
{
    /// <inheritdoc />
    public string Id => "wt.ki.naht";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Auswahlregel — siehe <c>Anbieterpruefung</c>.</remarks>
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
        Rechtsbezuege.AnhangIII
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typ);
        ArgumentNullException.ThrowIfNull(erwartet);

        var gefunden = typ
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(eigenschaft => eigenschaft.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var dazu = gefunden.Except(erwartet, StringComparer.Ordinal).ToList();
        var fehlend = erwartet.Except(gefunden, StringComparer.Ordinal).ToList();

        if (dazu.Count == 0 && fehlend.Count == 0)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Pass,
                $"Der Kontext {wofuer} trägt genau {gefunden.Count} Felder: "
                + $"{string.Join(", ", gefunden)}. Mehr verlässt diesen Dienst "
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
        SecurityCheckStatus stand, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, stand, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
