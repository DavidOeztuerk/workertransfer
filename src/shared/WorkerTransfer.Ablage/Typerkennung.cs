namespace WorkerTransfer.Ablage;

/// <summary>Was für eine Datei das ist — laut ihren ersten Bytes.</summary>
/// <remarks>
/// <strong>Nie nach dem, was der Aufrufer behauptet.</strong> Ein
/// <c>Content-Type</c>-Kopf und eine Dateiendung sind beide frei wählbar; die
/// Signatur am Anfang der Datei ist es nicht. Wer der Behauptung glaubt, nimmt
/// eine ausführbare Datei entgegen, weil jemand sie <c>zeugnis.png</c> genannt
/// hat.
/// <para>
/// Erlaubt sind drei Typen — dieselben drei wie in ADR-0021. Jeder weitere ist
/// eine Entscheidung, keine Erweiterung: SVG zum Beispiel ist ein Dokument mit
/// Skripten und gehört nicht dazu, obwohl es wie ein Bild aussieht.
/// </para>
/// </remarks>
public static class Typerkennung
{
    /// <summary>Die drei erlaubten Typen.</summary>
    public static readonly IReadOnlyList<string> Erlaubt = ["image/png", "image/jpeg", "application/pdf"];

    /// <summary>Der Typ, oder <c>null</c>, wenn es keiner der erlaubten ist.</summary>
    public static string? Erkenne(ReadOnlySpan<byte> inhalt)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A — acht Bytes, absichtlich so gewählt,
        // dass eine Übertragung, die Zeilenenden umschreibt, sie zerstört.
        if (Beginnt(inhalt, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        // JPEG: FF D8 FF. Das vierte Byte unterscheidet die Varianten (JFIF,
        // Exif, …) und wird deshalb nicht geprüft.
        if (Beginnt(inhalt, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        // PDF: "%PDF-"
        return Beginnt(inhalt, [0x25, 0x50, 0x44, 0x46, 0x2D]) ? "application/pdf" : null;
    }

    /// <summary>Die übliche Endung zum Typ — für einen Dateinamen beim Abruf.</summary>
    public static string Endung(string typ) => typ switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "application/pdf" => ".pdf",
        _ => string.Empty
    };

    private static bool Beginnt(ReadOnlySpan<byte> inhalt, ReadOnlySpan<byte> signatur) =>
        inhalt.Length >= signatur.Length && inhalt[..signatur.Length].SequenceEqual(signatur);
}
