using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Ablage;

/// <summary>Wo die lokale Ablage liegt.</summary>
public sealed class DocumentStoreSettings
{
    /// <summary>Der Section in der Konfiguration.</summary>
    public const string Section = "Ablage";

    /// <summary>Das Verzeichnis. In Containern ein Band, sonst ein Ordner.</summary>
    public string Verzeichnis { get; set; } = "ablage";
}

/// <summary>Die Ablage auf dem Dateisystem.</summary>
/// <remarks>
/// <strong>Schreiben unter Zwischennamen, dann umbenennen.</strong> Direkt zu
/// schreiben heisst: ein Absturz mittendrin hinterlaesst eine halbe Datei unter
/// dem richtigen Namen — und die sieht fuer jeden Leser gueltig aus. Das
/// Umbenennen innerhalb eines Dateisystems ist unteilbar; entweder liegt die
/// ganze Datei da oder gar keine.
/// </remarks>
public sealed class LocalDocumentStore : IDocumentStore
{
    private readonly string _wurzel;

    /// <summary>Baut die Ablage und legt ihr Verzeichnis an.</summary>
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
            // Der Zwischenname darf nicht liegen bleiben: er traegt keinen
            // gueltigen Schluessel, also findet ihn nie wieder jemand.
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
    /// Der Pfad zum Schluessel — und die Job, an der ein Ausbruch scheitert.
    /// </summary>
    /// <remarks>
    /// Ein Schluessel wie <c>../../etc/passwd</c> waere sonst ein Leseweg durch
    /// das ganze Dateisystem. Geprueft wird nicht der Text, sondern das
    /// ERGEBNIS: nach dem Zusammensetzen muss der volle Pfad unter der Wurzel
    /// liegen. Eine Textpruefung uebersieht die naechste Schreibweise, ein
    /// Vergleich der aufgeloesten Pfade nicht.
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

/// <summary>Haengt die Ablage in einen Dienst.</summary>
public static class DocumentStoreWiring
{
    /// <summary>Registriert die lokale Ablage samt Einstellungen.</summary>
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
