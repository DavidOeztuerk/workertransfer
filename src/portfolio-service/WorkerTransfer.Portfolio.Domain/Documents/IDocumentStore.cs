using Noelia.Core.Identity;

namespace WorkerTransfer.Portfolio.Domain.Documents;

/// <summary>A stored file, the way it is delivered.</summary>
/// <param name="Content">The stream. The caller closes it.</param>
/// <param name="MediaType">What the browser should do with it.</param>
public sealed record StoredFile(Stream Content, string MediaType) : IAsyncDisposable
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>Where the attachments lie.</summary>
/// <remarks>
/// <b>One interface, one implementation.</b> The predecessor package declared
/// five heavy dependencies — pillow, python-magic, boto3, minio, azure — for
/// zero consumers, and exactly that made it unbuildable (ADR-0021). As long as
/// no environment demands an object store, there is none here.
/// <para>
/// The key is formed from <c>SubjectId</c> and name and never from an input.
/// That is the structure that keeps anybody from reaching somebody else's file
/// with a foreign name.
/// </para>
/// </remarks>
public interface IDocumentStore
{
    /// <summary>Stores a file and returns the name it lies under.</summary>
    /// <remarks>
    /// The name is assigned <em>here</em>, not sent by the client: a name
    /// chosen by the caller is a path somebody can shape.
    /// </remarks>
    Task<string> PutAsync(
        SubjectId subject,
        string fileName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a file, or <c>null</c> when there is none.</summary>
    Task<StoredFile?> GetAsync(
        SubjectId subject, string name, CancellationToken cancellationToken = default);

    /// <summary>Erases everything stored for this person.</summary>
    /// <returns>How many files disappeared.</returns>
    /// <remarks>
    /// A file still on the disk after the erasure is exactly the silent failure
    /// ADR-0027 stands against: the row is gone, the work sample is not.
    /// </remarks>
    Task<int> DeleteAllAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
