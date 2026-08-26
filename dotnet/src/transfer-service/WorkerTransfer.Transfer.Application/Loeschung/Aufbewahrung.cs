using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Application.Loeschung;

/// <summary>Der zweite Schalter, der auf AUS steht (ADR-0027 §3).</summary>
/// <remarks>
/// <strong>Die Voreinstellung löscht auch bezahlte Transfers.</strong> Ein
/// Betrag, auf den sich zwei Unternehmen geeinigt haben, macht die Zeile nicht
/// zur Unterlage eines Vermittlers: die Plattform führt kein Geld, sie hält eine
/// Zahl fest, damit beide Seiten dieselbe im Blick haben.
/// </remarks>
public static class Aufbewahrung
{
    /// <summary><strong>AUS.</strong> Bezahlte Vorgänge fallen mit.</summary>
    /// <remarks>
    /// Eine benannte Konstante und <strong>kein Konfigurationswert</strong>: bei
    /// einem Löschversprechen wäre „in Produktion anders als im Test" der
    /// schlimmste denkbare Zustand.
    /// <para>
    /// Sie schaltet <strong>genau eine Zeilenklasse in diesem Dienst</strong>:
    /// ein abgeschlossener Handel <em>mit</em> Vergütung. Keine Ausdehnung auf
    /// <c>interested</c>/<c>talking</c>/<c>offered</c> — ein Gespräch ist kein
    /// Vertrag — und kein „laufender Vorgang" als Gummiwort. Ohne Vergütung ist
    /// kein Handelsvorgang entstanden, an dem etwas hängen könnte. Und keine
    /// Frist, in keiner Richtung: wird sie je umgelegt, kommt die Frist
    /// <em>zusammen mit der Antwort</em>.
    /// </para>
    /// <para>
    /// <c>static readonly</c> und nicht <c>const</c>, aus demselben gemessenen
    /// Grund wie bei applications-service: ein <c>const</c> wird in jede lesende
    /// Assembly hineinkopiert, und eine Gegenprobe fiel dadurch über
    /// unverändertem Code.
    /// </para>
    /// </remarks>
    public static readonly bool BezahlteBehalten = false;

    /// <summary>Die Stände, an denen ein bezahlter Vorgang hängen könnte.</summary>
    /// <remarks>
    /// <strong>Nicht</strong> die Endzustände des Aggregats: die enthalten
    /// <c>declined</c> und <c>withdrawn</c>, und ein abgesagter Vorgang
    /// begründet nichts. Ausgeschrieben, weil das Abgrenzen der ganze Punkt ist
    /// (ADR-0027 §3.2).
    /// </remarks>
    public static readonly IReadOnlyList<Transferstand> Abgeschlossene =
        [Transferstand.Accepted, Transferstand.Completed];
}
