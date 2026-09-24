namespace WorkerTransfer.DocumentStore;

/// <summary>What kind of file this is — according to its first bytes.</summary>
/// <remarks>
/// <strong>Never by what the caller claims.</strong> A <c>Content-Type</c>
/// header and a file extension are both freely chosen; the signature at the
/// start of the file is not. Whoever believes the claim accepts an executable
/// because somebody named it <c>certificate.png</c>.
/// <para>
/// Three types are allowed — the same three as in ADR-0021. Every further one
/// is a decision, not an extension: SVG, for example, is a document with
/// scripts and does not belong here, although it looks like an image.
/// </para>
/// </remarks>
public static class ContentTypeDetection
{
    /// <summary>The three allowed types.</summary>
    public static readonly IReadOnlyList<string> Allowed = ["image/png", "image/jpeg", "application/pdf"];

    /// <summary>The type, or <c>null</c> if it is none of the allowed ones.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A — eight bytes, deliberately chosen so
        // that a transfer which rewrites line endings destroys them.
        if (StartsWith(content, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        // JPEG: FF D8 FF. The fourth byte distinguishes the variants (JFIF,
        // Exif, …) and is therefore not checked.
        if (StartsWith(content, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        // PDF: "%PDF-"
        return StartsWith(content, [0x25, 0x50, 0x44, 0x46, 0x2D]) ? "application/pdf" : null;
    }

    /// <summary>The usual extension for the type — for a file name on retrieval.</summary>
    public static string Extension(string type) => type switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "application/pdf" => ".pdf",
        _ => string.Empty
    };

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature) =>
        content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);
}
