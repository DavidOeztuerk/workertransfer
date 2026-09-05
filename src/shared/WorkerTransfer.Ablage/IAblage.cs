namespace WorkerTransfer.Ablage;

/// <summary>Wohin Dateien gelegt werden, die kein Feld fassen kann.</summary>
/// <remarks>
/// <strong>Der Port aus ADR-0021, zurück in .NET.</strong> Das Python-Paket
/// hatte fünf schwere Abhängigkeiten für Backends, die niemand benutzte — S3,
/// MinIO, Azure Blob, Pillow, python-magic —, und wurde deshalb auf einen Port
/// und ein Backend zurückgeschnitten, das wirklich läuft. Genau das steht hier
/// wieder, und keinen Schritt weiter: <strong>kein S3, noch nicht.</strong> Es
/// zu bauen, bevor eine Umgebung es braucht, wäre der Fehler, der zu ADR-0021
/// geführt hat.
/// </remarks>
public interface IAblage
{
    /// <summary>Legt Bytes unter einem Schlüssel ab. Ersetzt, was dort lag.</summary>
    Task LegeAbAsync(
        string schluessel, ReadOnlyMemory<byte> inhalt, CancellationToken cancellationToken = default);

    /// <summary>Holt Bytes, oder <c>null</c>.</summary>
    /// <remarks>
    /// <c>null</c> statt einer Ausnahme: „gibt es nicht" ist beim Abrufen ein
    /// normaler Ausgang und kein Fehler. Wer daraus einen macht, zwingt jeden
    /// Aufrufer zu einem <c>try</c> um den Normalfall.
    /// </remarks>
    Task<byte[]?> HoleAsync(string schluessel, CancellationToken cancellationToken = default);

    /// <summary>Löscht. Schweigt über einen unbekannten Schlüssel.</summary>
    /// <remarks>
    /// Aufräumpfade sollen keinen Unterschied behandeln müssen, der sie nicht
    /// interessiert: ob die Datei noch da war, ändert nichts daran, dass sie
    /// danach weg sein soll.
    /// </remarks>
    Task LoescheAsync(string schluessel, CancellationToken cancellationToken = default);
}
