using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Ablage;

/// <summary>Wo die lokale Ablage liegt.</summary>
public sealed class Ablageeinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Ablage";

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
public sealed class LokaleAblage : IAblage
{
    private readonly string _wurzel;

    /// <summary>Baut die Ablage und legt ihr Verzeichnis an.</summary>
    public LokaleAblage(IOptions<Ablageeinstellungen> einstellungen)
    {
        ArgumentNullException.ThrowIfNull(einstellungen);

        _wurzel = Path.GetFullPath(einstellungen.Value.Verzeichnis);
        Directory.CreateDirectory(_wurzel);
    }

    /// <inheritdoc />
    public async Task LegeAbAsync(
        string schluessel, ReadOnlyMemory<byte> inhalt, CancellationToken cancellationToken = default)
    {
        var ziel = Pfad(schluessel);
        Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);

        var unterwegs = ziel + ".teil-" + Guid.CreateVersion7().ToString("N");

        try
        {
            await File.WriteAllBytesAsync(unterwegs, inhalt, cancellationToken);
            File.Move(unterwegs, ziel, overwrite: true);
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
    public async Task<byte[]?> HoleAsync(
        string schluessel, CancellationToken cancellationToken = default)
    {
        var pfad = Pfad(schluessel);

        return File.Exists(pfad)
            ? await File.ReadAllBytesAsync(pfad, cancellationToken)
            : null;
    }

    /// <inheritdoc />
    public Task LoescheAsync(string schluessel, CancellationToken cancellationToken = default)
    {
        var pfad = Pfad(schluessel);

        if (File.Exists(pfad))
        {
            File.Delete(pfad);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Der Pfad zum Schluessel — und die Stelle, an der ein Ausbruch scheitert.
    /// </summary>
    /// <remarks>
    /// Ein Schluessel wie <c>../../etc/passwd</c> waere sonst ein Leseweg durch
    /// das ganze Dateisystem. Geprueft wird nicht der Text, sondern das
    /// ERGEBNIS: nach dem Zusammensetzen muss der volle Pfad unter der Wurzel
    /// liegen. Eine Textpruefung uebersieht die naechste Schreibweise, ein
    /// Vergleich der aufgeloesten Pfade nicht.
    /// </remarks>
    private string Pfad(string schluessel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);

        var voll = Path.GetFullPath(Path.Combine(_wurzel, schluessel));

        if (!voll.StartsWith(_wurzel + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("key escapes the storage root", nameof(schluessel));
        }

        return voll;
    }
}

/// <summary>Haengt die Ablage in einen Dienst.</summary>
public static class Ablageverdrahtung
{
    /// <summary>Registriert die lokale Ablage samt Einstellungen.</summary>
    public static IServiceCollection AddAblage(
        this IServiceCollection dienste, Microsoft.Extensions.Configuration.IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(dienste);
        ArgumentNullException.ThrowIfNull(konfiguration);

        dienste.Configure<Ablageeinstellungen>(
            konfiguration.GetSection(Ablageeinstellungen.Abschnitt));
        dienste.AddSingleton<IAblage, LokaleAblage>();

        return dienste;
    }
}
