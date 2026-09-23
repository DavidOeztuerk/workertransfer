using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Documents;

/// <summary>What was read in a document — on request, once.</summary>
/// <remarks>
/// <strong>The index from PBI-7, and it lives here because the documents lie
/// here.</strong> Not in profile-service, not in scout-service: an index over
/// personal texts may only exist at all as long as it stands in the same service
/// as the files it came from — and is therefore hit by the same erasure
/// (ADR-0027, ADR-0043).
/// <para>
/// <strong>Names are stored, never the text.</strong> The full text found lives
/// in the memory of the one call and is thrown away afterwards. A column with
/// the wording of a reference would be a second copy of the document — in the
/// database instead of in the document store, and therefore in every backup and
/// in every dump somebody pulls for debugging (the same reasoning as in
/// <see cref="DocumentRecord"/>).
/// </para>
/// <para>
/// <strong>A term is EVIDENCE, not a naming</strong> (ADR-0033). It is a
/// statement about a <em>document</em> — this word stands in this file — and
/// never becomes searchable. Searchable is only what the person typed into their
/// profile and saved themselves; between the term and that statement lie two
/// acts in the browser.
/// </para>
/// </remarks>
public sealed class DocumentTerms
{
    private readonly List<string> _terms;

    private DocumentTerms(
        Guid id,
        SubjectId subject,
        Guid documentId,
        bool textFound,
        IEnumerable<string> terms,
        DateTimeOffset readAt)
    {
        Id = id;
        Subject = subject;
        DocumentId = documentId;
        TextFound = textFound;
        _terms = [.. terms];
        ReadAt = readAt;
    }

    /// <summary>The id of this row.</summary>
    public Guid Id { get; }

    /// <summary>Whose document was read.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which document.</summary>
    public Guid DocumentId { get; }

    /// <summary>Was there any text to read at all?</summary>
    /// <remarks>
    /// <strong>The difference between "nothing found" and "nothing read", and
    /// it is no subtlety.</strong> A photographed journeyman's certificate is a
    /// picture: it holds no text a reader without OCR would find. Showing both
    /// cases as "no suggestions" would tell a human being silently that their
    /// master craftsman's certificate says nothing — that is ADR-0022 §3, a lie
    /// by omission.
    /// </remarks>
    public bool TextFound { get; }

    /// <summary>The canonical names that occurred in the text. May be empty.</summary>
    public IReadOnlyList<string> Terms => _terms;

    /// <summary>When it was read.</summary>
    /// <remarks>
    /// A row without this moment does not exist: "not read yet" is the
    /// <em>absence</em> of the row, not an empty field in it.
    /// </remarks>
    public DateTimeOffset ReadAt { get; }

    /// <summary>Records what a triggered read yielded.</summary>
    /// <param name="subject">Whom the document belongs to.</param>
    /// <param name="documentId">Which one.</param>
    /// <param name="textFound">Whether there was any text to read.</param>
    /// <param name="terms">The canonical names from the text.</param>
    /// <param name="now">The moment.</param>
    public static DocumentTerms Record(
        SubjectId subject,
        Guid documentId,
        bool textFound,
        IEnumerable<string> terms,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(terms);

        return new DocumentTerms(
            Guid.CreateVersion7(), subject, documentId, textFound, terms, now);
    }

    /// <summary>The terms as a row holds them. Checks nothing.</summary>
    public static DocumentTerms Restore(
        Guid id,
        SubjectId subject,
        Guid documentId,
        bool textFound,
        IEnumerable<string> terms,
        DateTimeOffset readAt) =>
        new(id, subject, documentId, textFound, terms, readAt);
}

/// <summary>Keeps the terms. Two questions, and there is no third.</summary>
/// <remarks>
/// <strong>The method set IS the promise</strong>, and <c>ConditionsTests</c>
/// compares it as a set rather than by containment. What can be asked is "what
/// stands in THIS human being's documents" — never "which human beings have the
/// word X in their documents". The second question would be a search over other
/// people's documents and thereby the opposite of this whole piece of work:
/// searchable is only what somebody named themselves (ADR-0033).
/// </remarks>
public interface ITermStore
{
    /// <summary>This person's terms.</summary>
    Task<IReadOnlyList<DocumentTerms>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Stores the terms and replaces earlier ones for the same document.</summary>
    Task SaveAsync(DocumentTerms terms, CancellationToken cancellationToken = default);

    /// <summary>Removes the terms for this document.</summary>
    /// <remarks>
    /// For the path on which the document itself disappears: terms without
    /// their file would be the wording of a deleted reference that stays
    /// behind.
    /// </remarks>
    Task DeleteAsync(
        SubjectId subject, Guid documentId, CancellationToken cancellationToken = default);
}
