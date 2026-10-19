namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>What could be read from a file.</summary>
/// <param name="TextFound">
/// Whether there was any text in it at all. A photo of a journeyman's
/// certificate is a picture; it holds no text, and that is something other than
/// a text without known words.
/// </param>
/// <param name="Text">
/// The wording — <strong>transient</strong>. It is never stored, never logged,
/// and never leaves the call in which it came into being.
/// </param>
public sealed record RecognisedText(bool TextFound, string Text);

/// <summary>Reads text from the bytes of a document.</summary>
/// <remarks>
/// <strong>A port, so that the decision "which text recogniser" stays a
/// decision</strong> — the same seam as <c>IDrafter</c> for the AI (ADR-0024).
/// Today's implementation is a library in the process and calls nobody;
/// whoever ever changes that visibly changes the registration in the
/// composition root and must write the destination into the configuration —
/// otherwise the sovereignty boundary refuses it without logging a line
/// (ADR-0043).
/// <para>
/// <strong>Synchronous, and that is no oversight.</strong> A <c>Task</c> here
/// would be the invitation to put a network call behind this port — and exactly
/// that must not be able to happen in passing.
/// </para>
/// </remarks>
public interface ITextRecogniser
{
    /// <summary>Reads. Does not throw: an unreadable file is no error, but empty.</summary>
    /// <param name="content">The bytes.</param>
    /// <param name="contentType">The type, as the signature check showed it.</param>
    RecognisedText Read(ReadOnlyMemory<byte> content, string contentType);
}
