using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>Die Unterlage taugt nicht.</summary>
public sealed class Unterlagenfehler(string was) : Exception(was);

/// <summary>Was für eine Unterlage das ist.</summary>
/// <remarks>
/// Drei Arten und keine vierte „Sonstiges mit Unterart". Die Art ordnet für
/// den Menschen, der die Mappe liest; sie wird nirgends ausgewertet und trägt
/// keine Wertung — ein Zertifikat ist nicht mehr wert als ein Zeugnis.
/// </remarks>
public enum Unterlagenart
{
    /// <summary>Arbeitszeugnis, Abschlusszeugnis.</summary>
    Zeugnis,

    /// <summary>Kurs, Prüfung, Nachweis.</summary>
    Zertifikat,

    /// <summary>Alles andere, was jemand beilegen will.</summary>
    Sonstiges
}

/// <summary>Eine beigelegte Datei — Zeugnis, Zertifikat, sonst etwas.</summary>
/// <remarks>
/// <strong>Die Zeile hält alles außer den Bytes.</strong> Die liegen in der
/// Ablage (ADR-0035), und diese Trennung ist nicht Bequemlichkeit: eine
/// Datenbank, in der Dateien liegen, wandert vollständig in jede Sicherung und
/// in jeden Abzug, den irgendwer einmal für eine Fehlersuche zieht.
/// <para>
/// <strong>Der Inhaltstyp kommt aus den Bytes</strong>, nicht aus dem, was der
/// Aufrufer behauptet — geprüft wird das eine Ebene höher, und hier steht nur,
/// was dabei herauskam. Ein Feld, das eine Behauptung des Aufrufers speichert,
/// sähe genauso aus und wäre wertlos.
/// </para>
/// </remarks>
public sealed class Unterlage
{
    /// <summary>Wie lang der Name sein darf, den die Person vergibt.</summary>
    public const int HoechstlaengeName = 120;

    /// <summary>Wie viele Unterlagen eine Person halten darf.</summary>
    /// <remarks>
    /// Ohne Grenze baut ein Aufrufer mit einer Schleife eine beliebig teure
    /// Ablage. Und eine Bewerbung mit dreißig Anhängen liest ohnehin niemand.
    /// </remarks>
    public const int HoechsteAnzahl = 10;

    /// <summary>Wie groß eine Datei sein darf.</summary>
    public const int HoechsteGroesse = 5 * 1024 * 1024;

    private Unterlage(
        Guid id,
        SubjectId wer,
        string name,
        Unterlagenart art,
        string inhaltstyp,
        int groesse,
        string ablageschluessel,
        DateTimeOffset hochgeladen)
    {
        Id = id;
        Wer = wer;
        Name = name;
        Art = art;
        Inhaltstyp = inhaltstyp;
        Groesse = groesse;
        Ablageschluessel = ablageschluessel;
        Hochgeladen = hochgeladen;
    }

    /// <summary>Die Kennung dieser Unterlage.</summary>
    public Guid Id { get; }

    /// <summary>Wem sie gehört.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wie die Person sie genannt hat.</summary>
    public string Name { get; }

    /// <summary>Zeugnis, Zertifikat oder sonst etwas.</summary>
    public Unterlagenart Art { get; }

    /// <summary>Der Typ, wie ihn die Signatur ergab.</summary>
    public string Inhaltstyp { get; }

    /// <summary>Wie viele Bytes.</summary>
    public int Groesse { get; }

    /// <summary>Unter welchem Schlüssel die Bytes liegen.</summary>
    public string Ablageschluessel { get; }

    /// <summary>Wann sie kam.</summary>
    public DateTimeOffset Hochgeladen { get; }

    /// <summary>Nimmt eine Unterlage an.</summary>
    /// <param name="wer">Wem sie gehört.</param>
    /// <param name="name">Der Name, den die Person vergibt.</param>
    /// <param name="art">Zeugnis, Zertifikat, sonst etwas.</param>
    /// <param name="inhaltstyp">Was die Signaturprüfung ergeben hat.</param>
    /// <param name="groesse">Wie viele Bytes.</param>
    /// <param name="jetzt">Der Zeitpunkt.</param>
    /// <exception cref="Unterlagenfehler">Name oder Größe taugen nicht.</exception>
    public static Unterlage Nimm_an(
        SubjectId wer,
        string? name,
        Unterlagenart art,
        string inhaltstyp,
        int groesse,
        DateTimeOffset jetzt)
    {
        var sauber = (name ?? string.Empty).Trim();

        if (sauber.Length == 0)
        {
            throw new Unterlagenfehler("document name must not be empty");
        }

        if (sauber.Length > HoechstlaengeName)
        {
            throw new Unterlagenfehler($"document name must not exceed {HoechstlaengeName} characters");
        }

        if (groesse <= 0)
        {
            throw new Unterlagenfehler("document must not be empty");
        }

        if (groesse > HoechsteGroesse)
        {
            throw new Unterlagenfehler($"document must not exceed {HoechsteGroesse} bytes");
        }

        var id = Guid.CreateVersion7();

        // Der Schluessel traegt die Person im Pfad — nicht als Geheimnis,
        // sondern damit ein Aufraeumen nach Person moeglich bleibt, ohne die
        // Zeilen zu befragen.
        return new Unterlage(
            id, wer, sauber, art, inhaltstyp, groesse,
            $"{wer.Value:N}/{id:N}", jetzt);
    }

    /// <summary>Die Unterlage, wie eine Zeile sie hält. Prüft nichts.</summary>
    /// <remarks>
    /// Der Weg des Speichers zurück. Bewusst ohne die Regeln: eine Zeile, die
    /// unter einer älteren Grenze geschrieben wurde, muss lesbar bleiben —
    /// jemandem seine Unterlage zu verbergen, weil sich der Code geändert hat,
    /// wäre die schlechtere Antwort.
    /// </remarks>
    public static Unterlage Stelle_her(
        Guid id,
        SubjectId wer,
        string name,
        Unterlagenart art,
        string inhaltstyp,
        int groesse,
        string ablageschluessel,
        DateTimeOffset hochgeladen) =>
        new(id, wer, name, art, inhaltstyp, groesse, ablageschluessel, hochgeladen);
}

/// <summary>Findet und speichert Unterlagen.</summary>
public interface IUnterlagenSpeicher
{
    /// <summary>Die Unterlagen dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Unterlage>> AlleAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Eine bestimmte — oder <c>null</c>.</summary>
    Task<Unterlage?> HoleAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Die genannten, in der Reihenfolge der Kennungen.</summary>
    /// <remarks>
    /// Für die Mappe: sie zeigt genau die Unterlagen, die eine Bewerbung
    /// beigelegt hat, und nicht alle, die jemand jemals hochgeladen hat.
    /// </remarks>
    Task<IReadOnlyList<Unterlage>> HoleVieleAsync(
        SubjectId wer, IReadOnlyList<Guid> kennungen, CancellationToken cancellationToken = default);

    /// <summary>Legt an.</summary>
    Task SichereAsync(Unterlage unterlage, CancellationToken cancellationToken = default);

    /// <summary>Löscht eine.</summary>
    Task LoescheAsync(SubjectId wer, Guid id, CancellationToken cancellationToken = default);
}
