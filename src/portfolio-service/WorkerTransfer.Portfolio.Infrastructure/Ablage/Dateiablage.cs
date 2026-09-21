using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Portfolio.Domain.Ablage;

namespace WorkerTransfer.Portfolio.Infrastructure.Ablage;

/// <summary>Wo die Anhänge liegen.</summary>
public sealed class DocumentStoreSettings
{
    /// <summary>Der Section in der Konfiguration.</summary>
    public const string Section = "Storage";

    /// <summary>Das Verzeichnis unter dem alles liegt.</summary>
    public string Wurzel { get; set; } = "/var/lib/workertransfer/portfolio";

    /// <summary>Wie groß eine Arbeitsprobe sein darf.</summary>
    /// <remarks>
    /// Zehn Megabyte. Eine Grenze, weil ohne sie die erste Videodatei den
    /// Datenträger füllt — und weil ein Portfolio ein Schaufenster ist und kein
    /// Archiv.
    /// </remarks>
    public long HoechsteGroesse { get; set; } = 10 * 1024 * 1024;
}

/// <summary>Eine Ablage, ein Backend: das Dateisystem.</summary>
/// <remarks>
/// Kein S3, kein Azure, kein MinIO, solange keine EnvironmentFile eines verlangt. Das
/// Vorgängerpaket erklärte fünf schwere Abhängigkeiten für null Konsumenten,
/// und genau das machte es unbaubar (ADR-0021).
/// <para>
/// Der Weg entsteht aus <c>SubjectId</c> und einem <em>hier</em> vergebenen
/// Namen. Nichts, was ein Aufrufer schickt, wird Teil eines Pfades.
/// </para>
/// </remarks>
public sealed class FileDocumentStore(IOptions<DocumentStoreSettings> einstellungen) : IDocumentStore
{
    private readonly DocumentStoreSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<string> PutAsync(
        SubjectId subject,
        string dateiname,
        string medientyp,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Der Name wird hier vergeben. Die Endung des Absenders wird
        // übernommen, weil sie dem Browser hilft — aber nur, wenn sie aus
        // Buchstaben und Ziffern besteht, sonst gar keine.
        var endung = Endung(dateiname);
        var name = $"{Guid.CreateVersion7():N}{endung}";
        var ordner = Verzeichnis(subject);

        Directory.CreateDirectory(ordner);

        var target = Path.Combine(ordner, name);

        await using (var file = File.Create(target))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        // Der Medientyp liegt daneben, nicht in der Datei: ihn aus dem Inhalt zu
        // raten braucht eine Bibliothek, und ihn beim Ausliefern zu raten wäre
        // schlimmer.
        await File.WriteAllTextAsync(target + ".typ", Bereinigt(medientyp), cancellationToken);

        return name;
    }

    /// <inheritdoc />
    public async Task<Abgelegtes?> GetAsync(
        SubjectId subject, string name, CancellationToken cancellationToken = default)
    {
        var target = Pfad(subject, name);

        if (target is null || !File.Exists(target))
        {
            return null;
        }

        var type = File.Exists(target + ".typ")
            ? await File.ReadAllTextAsync(target + ".typ", cancellationToken)
            : "application/octet-stream";

        return new Abgelegtes(File.OpenRead(target), type);
    }

    /// <inheritdoc />
    public Task<int> DeleteAllAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var ordner = Verzeichnis(subject);

        if (!Directory.Exists(ordner))
        {
            return Task.FromResult(0);
        }

        // Gezählt werden die Dateien, nicht die Typmarken daneben: gemeldet
        // wird, wie viele WorkSamples verschwunden sind.
        var wieViele = Directory.EnumerateFiles(ordner)
            .Count(file => !file.EndsWith(".typ", StringComparison.Ordinal));

        Directory.Delete(ordner, recursive: true);

        return Task.FromResult(wieViele);
    }

    private string Verzeichnis(SubjectId subject) =>
        Path.Combine(_einstellungen.Wurzel, subject.Value.ToString("N"));

    /// <summary>
    /// Der volle Pfad — oder <c>null</c>, wenn der Name keiner ist.
    /// </summary>
    /// <remarks>
    /// Zusammengesetzt und danach geprüft, dass das Ergebnis wirklich unter dem
    /// Verzeichnis der Person liegt. Die Domäne weist Pfade schon an der Grenze
    /// ab; das hier ist die zweite Wache, und sie steht, weil ein Weg aus einem
    /// fremden Verzeichnis heraus der teuerste denkbare Fehler wäre.
    /// </remarks>
    private string? Pfad(SubjectId subject, string name)
    {
        var ordner = Path.GetFullPath(Verzeichnis(subject));
        var target = Path.GetFullPath(Path.Combine(ordner, name));

        return target.StartsWith(ordner + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? target
            : null;
    }

    private static string Endung(string dateiname)
    {
        var endung = Path.GetExtension(dateiname ?? string.Empty);

        return endung.Length is > 1 and <= 10
               && endung[1..].All(char.IsLetterOrDigit)
            ? endung.ToLowerInvariant()
            : string.Empty;
    }

    private static string Bereinigt(string medientyp) =>
        string.IsNullOrWhiteSpace(medientyp)
        || medientyp.Any(zeichen => char.IsControl(zeichen) || zeichen is '\n' or '\r')
            ? "application/octet-stream"
            : medientyp.Trim();
}
