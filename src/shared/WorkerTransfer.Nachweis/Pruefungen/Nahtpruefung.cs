using System.Reflection;

namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Trägt der Kontext, der das Modell verlässt, mehr als vereinbart?</summary>
/// <remarks>
/// <para><strong>Der Laufzeit-Zwilling von <c>EntwurfsgrenzeTests</c>, und er
/// steht nicht umsonst daneben.</strong> Der Test prüft den Baum, aus dem
/// gebaut wurde; diese Prüfung prüft die Zusammenstellung, die gerade läuft.
/// Für eine Feldmenge ist der Unterschied klein — beide lesen denselben Typ —,
/// und er ist trotzdem der Punkt: was hier steht, steht im <em>Nachweis</em>.
/// Ein Mensch, der die Anhang-III-Frage beantworten muss, bekommt die
/// Feldmenge aufgeschrieben, statt sie in einem Testprojekt suchen zu
/// müssen.</para>
///
/// <para><strong>Gegen eine ausgeschriebene Menge, nicht gegen eine
/// Verbotsliste.</strong> Eine Verbotsliste beantwortet „ist dieser Name
/// heikel?“ — eine Frage, die man beim nächsten Feld neu und womöglich falsch
/// beantwortet. Eine geschlossene Menge verlangt, dass wer ein Feld hinzufügt,
/// es hier hinschreibt: dieselbe Bewegung, aber eine, die auffällt.</para>
///
/// <para>Die erwartete Menge steht im Dienst und nicht hier: <em>welche</em>
/// vier Felder hinausgehen dürfen, ist eine Entscheidung dieses Dienstes
/// (ADR-0024 §3, eigene Kontextklasse je Verbraucher). Eine gemeinsame Menge
/// wäre der erste Schritt zu einem gemeinsamen Prompt mit einem <c>if</c>.</para>
/// </remarks>
/// <param name="typ">Die Kontextklasse, die die Grenze IST.</param>
/// <param name="erwartet">Die vereinbarte Feldmenge.</param>
/// <param name="wofuer">Wofür der Kontext da ist, in drei Worten.</param>
public sealed class Nahtpruefung(Type typ, IReadOnlyList<string> erwartet, string wofuer)
    : IPruefung
{
    /// <inheritdoc />
    public string Id => "wt.ki.naht";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.KI;

    /// <inheritdoc />
    /// <remarks>
    /// Die Feldmenge ist der Beleg dafür, <em>was</em> über einen Menschen das
    /// Haus verlässt — die Tatsache, nach der Art. 30 Abs. 1 Buchst. d fragt,
    /// und die Grundlage, auf der jemand die Anhang-III-Frage beantwortet.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege =>
    [
        Rechtsbezuege.Verzeichnis,
        Rechtsbezuege.AnhangIII
    ];

    /// <inheritdoc />
    public Task<Befund> LaufenAsync(CancellationToken ct = default)
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
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.Erfuellt,
                $"Der Kontext {wofuer} trägt genau "
                + $"{Zielkunde.Zahl(gefunden.Count)} Felder: "
                + $"{string.Join(", ", gefunden)}. Mehr verlässt diesen Dienst "
                + "auf dem Weg zum Modell nicht.",
                "Ein Feld mehr an dieser Klasse schickt etwas Neues über einen "
                + "Menschen hinaus — und stößt diesen Befund um."));
        }

        return Task.FromResult(new Befund(
            Id,
            Bereich,
            Stand.Fehlt,
            $"Der Kontext {wofuer} weicht von der vereinbarten Feldmenge ab: "
            + (dazu.Count > 0 ? $"zusätzlich {string.Join(", ", dazu)}" : "")
            + (dazu.Count > 0 && fehlend.Count > 0 ? "; " : "")
            + (fehlend.Count > 0 ? $"es fehlen {string.Join(", ", fehlend)}" : "")
            + ".",
            "Entweder das Feld wieder entfernen, oder die vereinbarte Menge im "
            + "Verbundpunkt dieses Dienstes ergänzen — und dabei beantworten, "
            + "was mit diesem Feld über einen Menschen hinausgeht."));
    }
}
