namespace WorkerTransfer.Nachweis;

/// <summary>Worüber eine Prüfung Auskunft gibt.</summary>
/// <remarks>
/// Fünf Bereiche und nicht vierzehn Dienstnamen: ein Mensch, der diesen
/// Nachweis liest, sucht nach der Frage, die er beantworten muss, und nicht
/// danach, welcher Behälter sie beantwortet hat. Die Seiten in Abschnitt 3
/// des Auftrags sind genau diese Bereiche.
/// </remarks>
public enum Bereich
{
    /// <summary>Der Ledger als Tor — wirkt ein Widerruf beim nächsten Zugriff.</summary>
    Einwilligung,

    /// <summary>Alles, was ein Modell betrifft: Anbieter, Naht, Aufzeichnung.</summary>
    KI,

    /// <summary>Die Kaskade und was sie hinterlässt.</summary>
    Loeschung,

    /// <summary>Der Ledger als Bestand — was er über eine Person hält.</summary>
    Ledger,

    /// <summary>Wohin dieser Dienst spricht.</summary>
    Grenze
}

/// <summary>Wie eine Prüfung ihren Gegenstand vorgefunden hat.</summary>
/// <remarks>
/// <para><strong>Das sind keine Noten.</strong> Ein Artikel ist nichts, was
/// eine Prüfung bestehen kann (Auftrag, Abschnitt 3, Phase 3) — hier steht
/// ausschließlich, was beobachtet wurde:</para>
///
/// <list type="bullet">
/// <item><see cref="Erfuellt"/>: die Beobachtung liegt vor, und sie ist die
/// zurückhaltende — nichts verlässt das Haus, nichts steht zwischen Frage und
/// Ledger, die Feldmenge ist die vereinbarte.</item>
/// <item><see cref="Hinweis"/>: die Beobachtung liegt vor, und ein Mensch muss
/// etwas dazu entscheiden. Kein Mangel.</item>
/// <item><see cref="Fehlt"/>: eine Zusage dieses Baumes ist nicht eingelöst.
/// Nur hierauf geht <c>make nachweis-pruefen</c> rot.</item>
/// <item><see cref="NichtAnwendbar"/>: den Gegenstand gibt es hier nicht.</item>
/// </list>
///
/// <para><strong><see cref="NichtAnwendbar"/> und nicht
/// <see cref="Erfuellt"/></strong>, wenn der Gegenstand fehlt. Ein grüner Haken
/// an etwas, das gar nicht gilt, ist Rauschen in genau dem Dokument, das
/// Rauschen durchschneiden soll — und er addiert sich: vierzehn Dienste ohne
/// KI-Naht ergäben vierzehn grüne Haken über eine Naht, die es nicht gibt.</para>
/// </remarks>
public enum Stand
{
    /// <summary>Beobachtet, und es ist die zurückhaltende Lage.</summary>
    Erfuellt,

    /// <summary>Beobachtet, und ein Mensch entscheidet weiter.</summary>
    Hinweis,

    /// <summary>Eine Zusage dieses Baumes ist nicht eingelöst.</summary>
    Fehlt,

    /// <summary>Den Gegenstand gibt es in diesem Dienst nicht.</summary>
    NichtAnwendbar
}

/// <summary>Was eine Prüfung vorgefunden hat.</summary>
/// <remarks>
/// <para><strong><see cref="Zusammenfassung"/> und <see cref="Abhilfe"/> nennen
/// Gestalten und Handlungen, nie Werte.</strong> Kein Schlüssel, kein Token,
/// keine Verbindungszeichenfolge, kein roher Ausnahmetext — Anbieter schreiben
/// Endpunkte in ihre Ausnahmen, und ein Nachweis, den jemand herumreicht, ist
/// der schlechteste Ort dafür. <c>Nachweislauf</c> fängt deshalb jede Ausnahme
/// und setzt einen festen Satz ein, statt <c>ex.Message</c> durchzureichen.</para>
///
/// <para>Ein Hostname ist dabei ausdrücklich <em>kein</em> Wert in diesem
/// Sinne: er ist der Gegenstand des Verzeichnisses. Ein Name eines Menschen ist
/// einer, und deshalb zählt <c>wt.ki.anbieter</c> Menschen, statt sie zu
/// nennen (ADR-0026).</para>
/// </remarks>
/// <param name="Id">Die stabile, punktierte Kennung der Prüfung.</param>
/// <param name="Bereich">Auf welche Seite der Befund gehört.</param>
/// <param name="Stand">Wie der Gegenstand vorgefunden wurde.</param>
/// <param name="Zusammenfassung">Was beobachtet wurde — Gestalten, nie Werte.</param>
/// <param name="Abhilfe">
/// Was zu tun wäre. Bei <see cref="Stand.Erfuellt"/> steht hier, was den Befund
/// umstoßen würde — sonst liest sich ein grüner Befund wie ein Freibrief.
/// </param>
public sealed record Befund(
    string Id,
    Bereich Bereich,
    Stand Stand,
    string Zusammenfassung,
    string Abhilfe)
{
    /// <summary>Wonach ein Regelwerk fragt, das diesen Befund gebrauchen kann.</summary>
    /// <remarks>
    /// Sie hängen am Befund und nicht nur an der Prüfung, weil ein Lauf sie
    /// mitnimmt: die Pflichtenseite liest die Bezüge aus der Lesung, nicht aus
    /// dem Container.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege { get; init; } = [];
}
