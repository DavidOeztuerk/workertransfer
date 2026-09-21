using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Portfolio.Domain.Documents;

namespace WorkerTransfer.Portfolio.Infrastructure.Documents;

/// <summary>Where the attachments lie.</summary>
public sealed class DocumentStoreSettings
{
    /// <summary>The section in the configuration.</summary>
    public const string Section = "Storage";

    /// <summary>The directory everything lies under.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Storage__Wurzel</c>).
    /// </remarks>
    public string Wurzel { get; set; } = "/var/lib/workertransfer/portfolio";

    /// <summary>How large a work sample may be.</summary>
    /// <remarks>
    /// Ten megabytes. A limit, because without one the first video file fills
    /// the disk — and because a portfolio is a shop window and not an archive.
    /// </remarks>
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024;
}

/// <summary>One store, one backend: the file system.</summary>
/// <remarks>
/// No S3, no Azure, no MinIO, for as long as no environment demands one. The
/// predecessor package declared five heavy dependencies for zero consumers, and
/// exactly that made it unbuildable (ADR-0021).
/// <para>
/// The path is formed from <c>SubjectId</c> and a name assigned <em>here</em>.
/// Nothing a caller sends becomes part of a path.
/// </para>
/// </remarks>
public sealed class FileDocumentStore(IOptions<DocumentStoreSettings> settings) : IDocumentStore
{
    private readonly DocumentStoreSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<string> PutAsync(
        SubjectId subject,
        string fileName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The name is assigned here. The sender's extension is carried over
        // because it helps the browser — but only when it consists of letters
        // and digits, otherwise none at all.
        var extension = Extension(fileName);
        var name = $"{Guid.CreateVersion7():N}{extension}";
        var folder = FolderFor(subject);

        Directory.CreateDirectory(folder);

        var target = Path.Combine(folder, name);

        await using (var file = File.Create(target))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        // The media type lies beside the file, not inside it: guessing it from
        // the content needs a library, and guessing it at delivery time would
        // be worse.
        await File.WriteAllTextAsync(target + ".typ", Sanitised(mediaType), cancellationToken);

        return name;
    }

    /// <inheritdoc />
    public async Task<StoredFile?> GetAsync(
        SubjectId subject, string name, CancellationToken cancellationToken = default)
    {
        var target = FullPath(subject, name);

        if (target is null || !File.Exists(target))
        {
            return null;
        }

        var type = File.Exists(target + ".typ")
            ? await File.ReadAllTextAsync(target + ".typ", cancellationToken)
            : "application/octet-stream";

        return new StoredFile(File.OpenRead(target), type);
    }

    /// <inheritdoc />
    public Task<int> DeleteAllAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var folder = FolderFor(subject);

        if (!Directory.Exists(folder))
        {
            return Task.FromResult(0);
        }

        // The files are counted, not the type markers beside them: what is
        // reported is how many work samples disappeared.
        var howMany = Directory.EnumerateFiles(folder)
            .Count(file => !file.EndsWith(".typ", StringComparison.Ordinal));

        Directory.Delete(folder, recursive: true);

        return Task.FromResult(howMany);
    }

    private string FolderFor(SubjectId subject) =>
        Path.Combine(_settings.Wurzel, subject.Value.ToString("N"));

    /// <summary>
    /// The full path — or <c>null</c> when the name is not one.
    /// </summary>
    /// <remarks>
    /// Composed and afterwards checked that the result really lies under the
    /// person's directory. The domain refuses paths at the boundary already;
    /// this is the second guard, and it stands because a way out of somebody
    /// else's directory would be the most expensive fault imaginable.
    /// </remarks>
    private string? FullPath(SubjectId subject, string name)
    {
        var folder = Path.GetFullPath(FolderFor(subject));
        var target = Path.GetFullPath(Path.Combine(folder, name));

        return target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? target
            : null;
    }

    private static string Extension(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);

        return extension.Length is > 1 and <= 10
               && extension[1..].All(char.IsLetterOrDigit)
            ? extension.ToLowerInvariant()
            : string.Empty;
    }

    private static string Sanitised(string mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
        || mediaType.Any(character => char.IsControl(character) || character is '\n' or '\r')
            ? "application/octet-stream"
            : mediaType.Trim();
}
