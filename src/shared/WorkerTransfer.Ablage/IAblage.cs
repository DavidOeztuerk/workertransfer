namespace WorkerTransfer.Ablage;

/// <summary>Where files go that no field can hold.</summary>
/// <remarks>
/// <strong>The port from ADR-0021, back in .NET.</strong> The Python package
/// carried five heavy dependencies for backends nobody used — S3, MinIO, Azure
/// Blob, Pillow, python-magic — and was therefore cut back to one port and one
/// backend that actually runs. Exactly that stands here again, and not a step
/// further: <strong>no S3, not yet.</strong> Building it before an
/// EnvironmentFile needs it would be the very mistake that led to ADR-0021.
/// </remarks>
public interface IDocumentStore
{
    /// <summary>Stores bytes under a key. Replaces whatever lay there.</summary>
    Task PutAsync(
        string key, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>Fetches bytes, or <c>null</c>.</summary>
    /// <remarks>
    /// <c>null</c> rather than an exception: "does not exist" is a normal
    /// outcome of a fetch and not an error. Whoever makes one out of it forces
    /// every caller into a <c>try</c> around the normal case.
    /// </remarks>
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes. Stays silent about an unknown key.</summary>
    /// <remarks>
    /// Clean-up paths should not have to handle a difference that does not
    /// concern them: whether the file was still there changes nothing about it
    /// being gone afterwards.
    /// </remarks>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
