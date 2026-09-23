using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Documents;

/// <summary>The document is no good.</summary>
public sealed class DocumentError(string reason) : Exception(reason);

/// <summary>What kind of document this is.</summary>
/// <remarks>
/// The kind orders things for the human who reads the folder. A certificate is
/// worth no more than a reference. <see cref="DocumentKind.CurriculumVitae"/> is the other
/// way to put down one's career: a file instead of the typeset form — hence at
/// most one, and without a CSS template.
/// <para>
/// The members are the labels of the Postgres enum <c>document_kind</c>
/// (Npgsql derives them from the member names), so renaming one is a schema
/// change. On the wire the kinds travel as <c>zeugnis</c>, <c>zertifikat</c>,
/// <c>lebenslauf</c> and <c>sonstiges</c> — words mapped at the endpoint.
/// </para>
/// </remarks>
public enum DocumentKind
{
    /// <summary>Employment reference, school-leaving certificate.</summary>
    Reference,

    /// <summary>Course, examination, qualification.</summary>
    Certificate,

    /// <summary>Anything else somebody wants to enclose.</summary>
    Other,

    /// <summary>The CV as a file, rather than as a typeset form.</summary>
    /// <remarks>
    /// Npgsql derives the database label from this member name, so renaming it
    /// renamed the value in the <c>document_kind</c> enum too — from
    /// <c>lebenslauf</c> to <c>curriculum_vitae</c>. That is a schema change,
    /// and the migration creates the label; pinning the old one with
    /// <c>[PgName]</c> would have put the driver in the domain layer, which
    /// points the wrong way.
    /// </remarks>
    CurriculumVitae
}

/// <summary>An enclosed file — a reference, a certificate, something else.</summary>
/// <remarks>
/// <strong>The row holds everything except the bytes.</strong> Those lie in
/// the document store (ADR-0035), and the separation is not convenience: a
/// database holding files travels in full into every backup and into every dump
/// somebody ever pulls for debugging.
/// <para>
/// <strong>The content type comes from the bytes</strong>, not from what the
/// caller claims — that is checked one level up, and here stands only what came
/// of it. A field that stored the caller's claim would look exactly the same
/// and be worthless.
/// </para>
/// </remarks>
public sealed class DocumentRecord
{
    /// <summary>How long the name the person gives may be.</summary>
    public const int MaxNameLength = 120;

    /// <summary>How many documents a person may hold.</summary>
    /// <remarks>
    /// Without a limit a caller builds an arbitrarily expensive document store
    /// with a loop. And an application with thirty attachments nobody reads
    /// anyway.
    /// </remarks>
    public const int MaxDocuments = 10;

    /// <summary>How large a file may be.</summary>
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    private DocumentRecord(
        Guid id,
        SubjectId subject,
        string name,
        DocumentKind kind,
        string contentType,
        int sizeBytes,
        string storageKey,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        Subject = subject;
        Name = name;
        Kind = kind;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        UploadedAt = uploadedAt;
    }

    /// <summary>The id of this document.</summary>
    public Guid Id { get; }

    /// <summary>Whom it belongs to.</summary>
    public SubjectId Subject { get; }

    /// <summary>What the person called it.</summary>
    public string Name { get; }

    /// <summary>Reference, certificate, CV file or something else.</summary>
    public DocumentKind Kind { get; private set; }

    /// <summary>The type, as the signature showed it.</summary>
    public string ContentType { get; }

    /// <summary>How many bytes.</summary>
    public int SizeBytes { get; }

    /// <summary>Under which key the bytes lie.</summary>
    public string StorageKey { get; }

    /// <summary>When it came.</summary>
    public DateTimeOffset UploadedAt { get; }

    /// <summary>Gives this file another kind — the CV, for one. At most one may be that.</summary>
    public void Reclassify(DocumentKind kind) => Kind = kind;

    /// <summary>Accepts a document.</summary>
    /// <param name="subject">Whom it belongs to.</param>
    /// <param name="name">The name the person gives.</param>
    /// <param name="kind">Reference, certificate, something else.</param>
    /// <param name="contentType">What the signature check showed.</param>
    /// <param name="sizeBytes">How many bytes.</param>
    /// <param name="now">The moment.</param>
    /// <exception cref="DocumentError">The name or the size is no good.</exception>
    public static DocumentRecord Accept(
        SubjectId subject,
        string? name,
        DocumentKind kind,
        string contentType,
        int sizeBytes,
        DateTimeOffset now)
    {
        var clean = (name ?? string.Empty).Trim();

        if (clean.Length == 0)
        {
            throw new DocumentError("document name must not be empty");
        }

        if (clean.Length > MaxNameLength)
        {
            throw new DocumentError($"document name must not exceed {MaxNameLength} characters");
        }

        if (sizeBytes <= 0)
        {
            throw new DocumentError("document must not be empty");
        }

        if (sizeBytes > MaxSizeBytes)
        {
            throw new DocumentError($"document must not exceed {MaxSizeBytes} bytes");
        }

        var id = Guid.CreateVersion7();

        // The key carries the person in its path — not as a secret, but so that
        // a clean-up per person stays possible without asking the rows.
        return new DocumentRecord(
            id, subject, clean, kind, contentType, sizeBytes,
            $"{subject.Value:N}/{id:N}", now);
    }

    /// <summary>The document as a row holds it. Checks nothing.</summary>
    /// <remarks>
    /// The store's way back. Deliberately without the rules: a row written
    /// under an older limit must stay readable — hiding somebody's document
    /// from them because the code changed would be the worse answer.
    /// </remarks>
    public static DocumentRecord Restore(
        Guid id,
        SubjectId subject,
        string name,
        DocumentKind kind,
        string contentType,
        int sizeBytes,
        string storageKey,
        DateTimeOffset uploadedAt) =>
        new(id, subject, name, kind, contentType, sizeBytes, storageKey, uploadedAt);
}

/// <summary>Finds and stores documents.</summary>
public interface IDocumentRecordStore
{
    /// <summary>This person's documents, newest first.</summary>
    Task<IReadOnlyList<DocumentRecord>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>A particular one — or <c>null</c>.</summary>
    Task<DocumentRecord?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default);

    /// <summary>The named ones, in the order of the ids.</summary>
    /// <remarks>
    /// For the folder: it shows exactly the documents an application enclosed,
    /// and not all that somebody ever uploaded.
    /// </remarks>
    Task<IReadOnlyList<DocumentRecord>> GetManyAsync(
        SubjectId subject, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Creates or rewrites one.</summary>
    Task SaveAsync(DocumentRecord document, CancellationToken cancellationToken = default);

    /// <summary>Deletes one.</summary>
    Task DeleteAsync(SubjectId subject, Guid id, CancellationToken cancellationToken = default);
}
