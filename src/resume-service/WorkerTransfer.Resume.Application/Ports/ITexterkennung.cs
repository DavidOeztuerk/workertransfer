namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>Was aus einer Datei zu lesen war.</summary>
/// <param name="TextGefunden">
/// Ob überhaupt Text darin stand. Ein Foto eines Gesellenbriefs ist ein Bild;
/// darin steht kein Text, und das ist etwas anderes als ein Text ohne bekannte
/// Wörter.
/// </param>
/// <param name="Text">
/// Der Wortlaut — <strong>flüchtig</strong>. Er wird nie gespeichert, nie
/// protokolliert und verlässt den Aufruf nicht, in dem er entstanden ist.
/// </param>
public sealed record Erkanntes(bool TextGefunden, string Text);

/// <summary>Liest Text aus den Bytes einer Unterlage.</summary>
/// <remarks>
/// <strong>Ein Port, damit die Entscheidung „welcher Texterkenner" eine
/// Entscheidung bleibt</strong> — dieselbe Naht wie <c>IEntwerfer</c> bei der
/// KI (ADR-0024). Die heutige Umsetzung ist eine Bibliothek im Prozess und ruft
/// niemanden an; wer das je ändert, ändert sichtbar die Registrierung im
/// Kompositionswurzel und muss das Ziel in die Konfiguration schreiben, sonst
/// weist die Souveränitätsgrenze es ab, ohne eine Zeile zu protokollieren
/// (ADR-0043).
/// <para>
/// <strong>Synchron, und das ist kein Versehen.</strong> Ein <c>Task</c> hier
/// wäre die Einladung, hinter diesem Port einen Netzaufruf zu legen — und
/// genau das soll hier nicht beiläufig passieren können.
/// </para>
/// </remarks>
public interface ITexterkennung
{
    /// <summary>Liest. Wirft nicht: eine unlesbare Datei ist kein Fehler, sondern leer.</summary>
    /// <param name="inhalt">Die Bytes.</param>
    /// <param name="inhaltstyp">Der Typ, wie die Signaturprüfung ihn ergab.</param>
    Erkanntes Lies(ReadOnlyMemory<byte> inhalt, string inhaltstyp);
}
