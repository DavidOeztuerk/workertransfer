namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Wer den Anbieter eingerichtet hat.</summary>
public enum Herkunft
{
    /// <summary>
    /// Der Betreiber, in der Umgebung — ein Zugang, der für alle gilt.
    /// </summary>
    Betreiber,

    /// <summary>
    /// Die Person, in ihren Kontoeinstellungen — ein Zugang je Mensch.
    /// </summary>
    Person
}

/// <summary>Ein Anbieter, wie er in dieser Instanz wirklich in Gebrauch ist.</summary>
/// <remarks>
/// <para><strong><see cref="Menschen"/> zählt und nennt nicht.</strong> „Drei
/// Menschen auf <c>api.anthropic.com</c>, einer auf einem eigenen Server“ ist
/// eine Aussage über die Instanz. „Diese Person auf jenem Server“ wäre eine
/// Aussage über einen Menschen und gehört nicht in ein Dokument, das jemand
/// herumreicht — ADR-0026 sagt dasselbe für Ereignisse.</para>
///
/// <para>Bei <see cref="Herkunft.Betreiber"/> steht hier <c>0</c>: der Zugang
/// hängt an keiner Person, er gilt für jede. Die Zahl wäre dort keine Messung,
/// sondern eine Verlegenheit.</para>
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
/// einer Tabelle, in profile-service und jobs-service in der Umgebung. Die
/// Bibliothek sieht in keine Datenbank — sie bekommt gezählte Zeilen und macht
/// daraus einen Befund.
/// </remarks>
public interface IAnbieterquelle
{
    /// <summary>Was in dieser Instanz eingetragen ist, aggregiert.</summary>
    /// <param name="ct">Bricht ab, wenn der Aufrufer auflegt.</param>
    /// <returns>Die Zeilen. Leer heißt: hier ist nichts eingerichtet.</returns>
    Task<IReadOnlyList<Anbieterzeile>> LeseAsync(CancellationToken ct = default);
}

/// <summary>Welche KI-Anbieter sind in dieser Instanz tatsächlich in Gebrauch?</summary>
/// <remarks>
/// <para><strong>Das ist die Prüfung, die kein Test ersetzen kann.</strong> Die
/// Feldmengen des Baumes stehen fest, sobald er gebaut ist; der KI-Zugang
/// steht in <c>KiZugangV1</c> — Anbieter, Adresse, Modell und Schlüssel je
/// Person, zur Laufzeit aus den Kontoeinstellungen. Wohin dieser Baum heute
/// Abend spricht, weiß nur der Behälter, der läuft.</para>
///
/// <para><strong>Und genau deshalb ist der Befund personenbezogen.</strong> Er
/// nennt Anbieter und Host und <em>zählt</em>. Wer welchen Anbieter benutzt,
/// steht nirgends — weder hier noch in der Seite noch in
/// <c>bericht.json</c>.</para>
///
/// <para><strong>Ein Verzeichnis sagt, was heute eingetragen ist.</strong> Es
/// kann nicht sagen, was morgen eingetragen wird, denn die Person wählt ihren
/// Anbieter selbst. Ob daraus eine Liste erlaubter Anbieter folgen soll, ist
/// eine Produktfrage — sie nähme der Person eine Wahl, die das Produkt ihr
/// heute ausdrücklich lässt. Dieser Befund entscheidet sie nicht; er macht sie
/// sichtbar.</para>
/// </remarks>
/// <param name="quellen">Woher die Zeilen kommen. Keine heißt: hier gibt es keine KI.</param>
public sealed class Anbieterpruefung(IEnumerable<IAnbieterquelle> quellen) : IPruefung
{
    /// <inheritdoc />
    public string Id => "wt.ki.anbieter";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.KI;

    /// <inheritdoc />
    /// <remarks>
    /// Das Anbieterverzeichnis ist der Beleg, für den Art. 30 Abs. 1 Buchst. d
    /// gebaut ist. Alles Weitere — Vertrag, Land, Garantie — steht in einem
    /// Aktenschrank, und die drei Zitate sagen das.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege =>
    [
        Rechtsbezuege.Verzeichnis,
        Rechtsbezuege.Auftragsverarbeitung,
        Rechtsbezuege.Drittland,
        Rechtsbezuege.Transparenz
    ];

    /// <inheritdoc />
    public async Task<Befund> LaufenAsync(CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(quellen);

        var zeilen = new List<Anbieterzeile>();

        foreach (var quelle in quellen)
        {
            zeilen.AddRange(await quelle.LeseAsync(ct));
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
            return new Befund(
                Id,
                Bereich,
                Stand.NichtAnwendbar,
                "In dieser Instanz ist kein KI-Anbieter eingetragen. Ohne "
                + "Eintrag wird kein Modell gefragt, und die Oberfläche sagt "
                + "das, statt eine Vorlage auszugeben, die wie ein Vorschlag "
                + "aussieht.",
                "Wer einen einträgt, erscheint ab dann in diesem Verzeichnis — "
                + "als Etikett und Host, gezählt, nie namentlich.");
        }

        var draussen = gebuendelt
            .Where(zeile => Zielkunde.Wo(zeile.Host) == Lage.Draussen)
            .ToList();

        var beschreibung = string.Join("; ", gebuendelt.Select(Satz));

        return new Befund(
            Id,
            Bereich,
            draussen.Count > 0 ? Stand.Hinweis : Stand.Erfuellt,
            beschreibung + ".",
            draussen.Count > 0
                ? "Jeder Anbieter im öffentlichen Netz ist ein Empfänger im "
                  + "Sinne von Art. 30 Abs. 1 DSGVO. In den Aktenschrank "
                  + "gehören: der Auftragsverarbeitungsvertrag, das Land der "
                  + "Verarbeitung und — außerhalb der Union — die Garantie."
                : "Solange jeder Eintrag im eigenen Netz liegt, verlässt kein "
                  + "Wort das Haus. Ein Eintrag mit einer öffentlichen Adresse "
                  + "ändert das, und zwar ohne Codeänderung.");
    }

    /// <summary>Eine Zeile, wie ein Mensch sie liest.</summary>
    private static string Satz(Anbieterzeile zeile) =>
        zeile.Herkunft == Herkunft.Betreiber
            ? $"{zeile.Etikett} auf {zeile.Host} "
              + $"({Zielkunde.Wort(Zielkunde.Wo(zeile.Host))}), vom Betreiber "
              + "eingerichtet und für alle gültig"
            : $"{Zielkunde.Zahl(zeile.Menschen)} Mensch(en) auf {zeile.Host} "
              + $"über {zeile.Etikett} "
              + $"({Zielkunde.Wort(Zielkunde.Wo(zeile.Host))})";
}
