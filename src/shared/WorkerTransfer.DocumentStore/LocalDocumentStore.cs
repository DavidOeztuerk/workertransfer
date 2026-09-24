using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.DocumentStore;

/// <summary>Where the local document store lies.</summary>
/// <remarks>
/// The section and the property name ARE the environment key
/// (<c>Ablage__Verzeichnis</c>), so they stay German until the keys are renamed
/// everywhere at once — in compose, in the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class DocumentStoreSettings
{
    /// <summary>The section in the configuration.</summary>
    public const string Section = "Ablage";

    /// <summary>The directory. A volume in containers, a folder otherwise.</summary>
    public string Verzeichnis { get; set; } = "document-store";
}

/// <summary>The document store on the file system.</summary>
/// <remarks>
/// <strong>Write under an intermediate name, then rename.</strong> Writing
/// directly means: a crash half way through leaves half a file under the right
/// name — and that looks valid to every reader. Renaming within one file system
/// is indivisible; either the whole file is there or none of it.
/// </remarks>
public sealed class LocalDocumentStore : IDocumentStore
{
    private readonly string _root;

    /// <summary>Builds the store and creates its directory.</summary>
    public LocalDocumentStore(IOptions<DocumentStoreSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _root = Path.GetFullPath(settings.Value.Verzeichnis);
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public async Task PutAsync(
        string key, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        var target = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var inFlight = target + ".part-" + Guid.CreateVersion7().ToString("N");

        try
        {
            await File.WriteAllBytesAsync(inFlight, content, cancellationToken);
            File.Move(inFlight, target, overwrite: true);
        }
        catch
        {
            // The intermediate name must not be left behind: it carries no
            // valid key, so nobody would ever find it again.
            if (File.Exists(inFlight))
            {
                File.Delete(inFlight);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(
        string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);

        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The path to the key — and the place where an escape fails.
    /// </summary>
    /// <remarks>
    /// A key such as <c>../../etc/passwd</c> would otherwise be a read path
    /// through the whole file system. What is checked is not the text but the
    /// RESULT: after composing it, the full path must lie below the root. A
    /// text check misses the next spelling, a comparison of the resolved paths
    /// does not.
    /// </remarks>
    private string PathFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var full = Path.GetFullPath(Path.Combine(_root, key));

        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("key escapes the storage root", nameof(key));
        }

        return full;
    }
}

/// <summary>Hangs the document store into a service.</summary>
public static class DocumentStoreWiring
{
    /// <summary>Registers the local document store together with its settings.</summary>
    public static IServiceCollection AddDocumentStore(
        this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<DocumentStoreSettings>(
            configuration.GetSection(DocumentStoreSettings.Section));
        services.AddSingleton<IDocumentStore, LocalDocumentStore>();

        return services;
    }
}
