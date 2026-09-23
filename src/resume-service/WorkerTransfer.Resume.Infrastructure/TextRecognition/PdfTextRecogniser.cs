using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.TextRecognition;

/// <summary>Reads the text from a PDF — in its own process.</summary>
/// <remarks>
/// <para><strong>A library and not a service, and that is the decision of
/// ADR-0043.</strong> A reference names the employer, the duration, often the
/// grade and sometimes periods of illness. A text recogniser on the network
/// would mean: this document leaves the platform so that a stranger can look
/// for words in it — and the gain would be a suggestion in a form.
/// <c>PdfPig</c> (MIT) pulls <strong>zero</strong> third-party packages and
/// calls nobody; there is no address here that would have to stand in the
/// configuration, and therefore nothing at which the sovereignty boundary could
/// silently refuse the button. That is exactly what the cover-letter agent died
/// of on 10.09.2026.</para>
///
/// <para><strong>No OCR on images, and that is said rather than kept
/// quiet.</strong> A photographed master craftsman's certificate is a picture;
/// so is a PDF from a scanner without a text layer. Both yield
/// <c>TextFound = false</c>, and the interface then says "no text could be read
/// from it" instead of "nothing stands in it". The difference is ADR-0022 §3:
/// <em>whoever has nothing on GitHub is not worse, but elsewhere — a view that
/// does not say so lies by omission.</em> OCR would need native binaries and
/// language data in the image; that is a decision of its own and not an
/// extension.</para>
///
/// <para><strong>An unreadable file is not an error.</strong> A damaged,
/// encrypted or merely unusual PDF throws — and that is the normal case, not the
/// exception, because every file a human being ever uploaded lands here. If it
/// threw, the whole read over ten documents would fail on a single one. What is
/// logged is the <em>kind of failure</em>, never a content (ADR-0024 §4).</para>
/// </remarks>
public sealed class PdfTextRecogniser(ILogger<PdfTextRecogniser> log) : ITextRecogniser
{
    /// <summary>How many pages are read.</summary>
    /// <remarks>
    /// A reference does not have fifty pages. Without a limit a click pays for
    /// the computing time of a file whose page total the caller decides — ten
    /// documents of five megabytes each are room enough for that.
    /// </remarks>
    public const int MaxPages = 50;

    /// <summary>Nothing read — the outcome for every file without a text layer.</summary>
    private static readonly RecognisedText Nothing = new(TextFound: false, Text: string.Empty);

    /// <inheritdoc />
    public RecognisedText Read(ReadOnlyMemory<byte> content, string contentType)
    {
        if (contentType != "application/pdf")
        {
            return Nothing;
        }

        try
        {
            using var document = PdfDocument.Open(content.ToArray());

            var pieces = document.GetPages()
                .Take(MaxPages)
                // `GetWords()` instead of `Text`: PdfPig joins two separately
                // drawn text blocks without a space, and "MIG/MAG." and
                // "Weiterhin" would become "MIG/MAG.Weiterhin" — a word that no
                // longer ends at a word boundary. Measured.
                .SelectMany(page => page.GetWords().Select(word => word.Text));

            var text = string.Join(' ', pieces);

            return text.Length == 0 ? Nothing : new RecognisedText(TextFound: true, text);
        }
        catch (Exception error)
        {
            // NO CONTENT IN THE LOG — the kind of failure and nothing else. The
            // wording of a reference belongs in the same class as a résumé: it
            // stands in no log (product-scope).
            log.LogWarning(
                "A document could not be read: {FailureKind}", error.GetType().Name);

            return Nothing;
        }
    }
}
