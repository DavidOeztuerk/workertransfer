using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.Texterkennung;

/// <summary>Liest den Text aus einem PDF — im eigenen Prozess.</summary>
/// <remarks>
/// <para><strong>Eine Bibliothek und kein Dienst, und das ist die Entscheidung
/// von ADR-0043.</strong> Ein Zeugnis nennt den Arbeitgeber, die Dauer, oft die
/// Note und manchmal Krankheitszeiten. Ein Texterkenner im Netz hiesse: dieses
/// Dokument verlässt die Plattform, damit ein Fremder Wörter darin sucht — und
/// der Gewinn wäre ein Vorschlag im Formular. <c>PdfPig</c> (MIT) zieht
/// <strong>null</strong> Fremdpakete und ruft niemanden an; es gibt hier keine
/// Adresse, die in der Konfiguration stehen müsste, und deshalb auch nichts,
/// woran die Souveränitätsgrenze den Knopf stumm abweisen könnte. Genau daran
/// ist am 10.09.2026 der Anschreiben-Agent gestorben.</para>
///
/// <para><strong>Keine Texterkennung auf Bildern, und das wird gesagt statt
/// verschwiegen.</strong> Ein abfotografierter Meisterbrief ist ein Bild; ein
/// PDF aus einem Scanner ohne Textschicht ebenso. Beides liefert
/// <c>TextGefunden = false</c>, und die Oberfläche sagt dann „daraus war kein
/// Text zu lesen" statt „darin steht nichts". Der Unterschied ist ADR-0022 §3:
/// <em>wer nichts auf GitHub hat, ist nicht schlechter, sondern woanders — eine
/// Ansicht, die das nicht sagt, lügt durch Auslassung.</em> Für OCR bräuchte es
/// native Binärdateien und Sprachdaten im Bild; das ist eine eigene
/// Entscheidung und keine Erweiterung.</para>
///
/// <para><strong>Eine unlesbare Datei ist kein Fehler.</strong> Ein
/// beschädigtes, verschlüsseltes oder bloss fremdartiges PDF wirft — und das
/// ist der Normalfall, nicht der Ausnahmefall, weil hier jede Datei landet, die
/// ein Mensch je hochgeladen hat. Geworfen würde der ganze Lesevorgang über
/// zehn Unterlagen an einer einzigen scheitern. Protokolliert wird die
/// <em>Fehlerart</em>, nie ein Inhalt (ADR-0024 §4).</para>
/// </remarks>
public sealed class PdfTexterkennung(ILogger<PdfTexterkennung> protokoll) : ITexterkennung
{
    /// <summary>Wie viele Seiten gelesen werden.</summary>
    /// <remarks>
    /// Ein Zeugnis hat keine fünfzig Seiten. Ohne Grenze bezahlt ein Klick die
    /// Rechenzeit einer Datei, deren Seitenzahl der Aufrufer bestimmt — zehn
    /// Unterlagen zu je fünf Megabyte sind dafür genug Spielraum.
    /// </remarks>
    public const int HoechsteSeitenzahl = 50;

    /// <summary>Nichts gelesen — der Ausgang für jede Datei ohne Textschicht.</summary>
    private static readonly Erkanntes Nichts = new(TextGefunden: false, Text: string.Empty);

    /// <inheritdoc />
    public Erkanntes Lies(ReadOnlyMemory<byte> inhalt, string inhaltstyp)
    {
        if (inhaltstyp != "application/pdf")
        {
            return Nichts;
        }

        try
        {
            using var dokument = PdfDocument.Open(inhalt.ToArray());

            var stuecke = dokument.GetPages()
                .Take(HoechsteSeitenzahl)
                // `GetWords()` statt `Text`: PdfPig setzt zwei getrennt
                // gezeichnete Textblöcke ohne Leerzeichen aneinander, und aus
                // „MIG/MAG." und „Weiterhin" würde „MIG/MAG.Weiterhin" — ein
                // Wort, das an keiner Wortgrenze mehr endet. Gemessen.
                .SelectMany(seite => seite.GetWords().Select(wort => wort.Text));

            var text = string.Join(' ', stuecke);

            return text.Length == 0 ? Nichts : new Erkanntes(TextGefunden: true, text);
        }
        catch (Exception fehler)
        {
            // KEIN INHALT IM PROTOKOLL — die Fehlerart und sonst nichts. Der
            // Wortlaut eines Zeugnisses gehört in dieselbe Klasse wie ein
            // Lebenslauf: er steht in keinem Log (product-scope).
            protokoll.LogWarning(
                "Eine Unterlage liess sich nicht lesen: {Fehlerart}", fehler.GetType().Name);

            return Nichts;
        }
    }
}
