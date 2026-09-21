using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Ablage;

/// <summary>Where the local document store lies.</summary>
public sealed class DocumentStoreSettings
{
    /// <summary>The section in the configuration.</summary>
    public const string Section = "Ablage";

    /// <summary>The directory. A volume in containers, a folder otherwise.</summary>
    public string Verzeichnis { get; set; } = "ablage";
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
    private readonly string _wurzel;

    /// <summary>Builds the store and creates its directory.</summary>
    public LocalDocumentStore(IOptions<DocumentStoreSettings> einstellungen)
    {
        ArgumentNullException.ThrowIfNull(einstellungen);

        _wurzel = Path.GetFullPath(einstellungen.Value.Verzeichnis);
        Directory.CreateDirectory(_wurzel);
    }

    /// <inheritdoc />
    public async Task PutAsync(
        string key, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        var target = Pfad(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var unterwegs = target + ".teil-" + Guid.CreateVersion7().ToString("N");

        try
        {
            await File.WriteAllBytesAsync(unterwegs, content, cancellationToken);
            File.Move(unterwegs, target, overwrite: true);
        }
        catch
        {
            // The intermediate name must not be left behind: it carries no
            // valid key, so nobody would ever find it again.
            if (File.Exists(unterwegs))
            {
                File.Delete(unterwegs);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(
        string key, CancellationToken cancellationToken = default)
    {
        var path = Pfad(key);

        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Pfad(key);

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
    private string Pfad(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var voll = Path.GetFullPath(Path.Combine(_wurzel, key));

        if (!voll.StartsWith(_wurzel + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("key escapes the storage root", nameof(key));
        }

        return voll;
    }
}

/// <summary>Hangs the document store into a service.</summary>
public static class DocumentStoreWiring
{
    /// <summary>Registers the local document store together with its settings.</summary>
    public static IServiceCollection AddAblage(
        this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(konfiguration);

        services.Configure<DocumentStoreSettings>(
            konfiguration.GetSection(DocumentStoreSettings.Section));
        services.AddSingleton<IDocumentStore, LocalDocumentStore>();

        return services;
    }
}
