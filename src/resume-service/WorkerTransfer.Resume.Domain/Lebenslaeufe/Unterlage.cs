using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>Die DocumentRecord taugt nicht.</summary>
public sealed class DocumentError(string was) : Exception(was);

/// <summary>Was für eine DocumentRecord das ist.</summary>
/// <remarks>
/// Die Art ordnet für den Menschen, der die Mappe liest. Ein Certificate ist
/// nicht mehr wert als ein Reference. <see cref="Lebenslauf"/> ist der andere
/// Weg, den Werdegang zu hinterlegen: eine Datei statt des gesetzten
/// Formulars — deshalb höchstens eine, und ohne CSS-ResumeTemplate.
/// </remarks>
public enum DocumentKind
{
    /// <summary>Arbeitszeugnis, Abschlusszeugnis.</summary>
    Reference,

    /// <summary>Kurs, Prüfung, Nachweis.</summary>
    Certificate,

    /// <summary>Alles andere, was jemand beilegen will.</summary>
    Other,

    /// <summary>The CV as a file, rather than as a typeset form.</summary>
    /// <remarks>
    /// Npgsql derives the database label from this member name, so renaming it
    /// renamed the value in the `document_kind` enum too — from `lebenslauf` to
    /// `curriculum_vitae`. That is a schema change and it has its own migration;
    /// pinning the old label with <c>[PgName]</c> would have put the driver in
    /// the domain layer, which points the wrong way.
    /// </remarks>
    CurriculumVitae
}

/// <summary>Eine beigelegte Datei — Reference, Certificate, sonst etwas.</summary>
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
public sealed class DocumentRecord
{
    /// <summary>Wie lang der Name sein darf, den die Person vergibt.</summary>
    public const int HoechstlaengeName = 120;

    /// <summary>Wie viele Unterlagen eine Person halten darf.</summary>
    /// <remarks>
    /// Ohne Grenze baut ein Aufrufer mit einer Schleife eine beliebig teure
    /// Ablage. Und eine JobApplication mit dreißig Anhängen liest ohnehin niemand.
    /// </remarks>
    public const int HoechsteAnzahl = 10;

    /// <summary>Wie groß eine Datei sein darf.</summary>
    public const int HoechsteGroesse = 5 * 1024 * 1024;

    private DocumentRecord(
        Guid id,
        SubjectId subject,
        string name,
        DocumentKind kind,
        string inhaltstyp,
        int groesse,
        string ablageschluessel,
        DateTimeOffset hochgeladen)
    {
        Id = id;
        Wer = subject;
        Name = name;
        Art = kind;
        Inhaltstyp = inhaltstyp;
        Groesse = groesse;
        Ablageschluessel = ablageschluessel;
        Hochgeladen = hochgeladen;
    }

    /// <summary>Die Kennung dieser DocumentRecord.</summary>
    public Guid Id { get; }

    /// <summary>Wem sie gehört.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wie die Person sie genannt hat.</summary>
    public string Name { get; }

    /// <summary>Reference, Certificate, CurriculumVitae-Datei oder sonst etwas.</summary>
    public DocumentKind Art { get; private set; }

    /// <summary>Der Typ, wie ihn die Signatur ergab.</summary>
    public string Inhaltstyp { get; }

    /// <summary>Wie viele Bytes.</summary>
    public int Groesse { get; }

    /// <summary>Unter welchem Schlüssel die Bytes liegen.</summary>
    public string Ablageschluessel { get; }

    /// <summary>Wann sie kam.</summary>
    public DateTimeOffset Hochgeladen { get; }

    /// <summary>Macht diese Datei zum CurriculumVitae. Höchstens eine darf das sein.</summary>
    public void Ordne_zu(DocumentKind kind) => Art = kind;

    /// <summary>Nimmt eine DocumentRecord an.</summary>
    /// <param name="wer">Wem sie gehört.</param>
    /// <param name="name">Der Name, den die Person vergibt.</param>
    /// <param name="art">Reference, Certificate, sonst etwas.</param>
    /// <param name="inhaltstyp">Was die Signaturprüfung ergeben hat.</param>
    /// <param name="groesse">Wie viele Bytes.</param>
    /// <param name="jetzt">Der Zeitpunkt.</param>
    /// <exception cref="Unterlagenfehler">Name oder Größe taugen nicht.</exception>
    public static DocumentRecord Nimm_an(
        SubjectId subject,
        string? name,
        DocumentKind kind,
        string inhaltstyp,
        int groesse,
        DateTimeOffset now)
    {
        var sauber = (name ?? string.Empty).Trim();

        if (sauber.Length == 0)
        {
            throw new DocumentError("document name must not be empty");
        }

        if (sauber.Length > HoechstlaengeName)
        {
            throw new DocumentError($"document name must not exceed {HoechstlaengeName} characters");
        }

        if (groesse <= 0)
        {
            throw new DocumentError("document must not be empty");
        }

        if (groesse > HoechsteGroesse)
        {
            throw new DocumentError($"document must not exceed {HoechsteGroesse} bytes");
        }

        var id = Guid.CreateVersion7();

        // Der Schluessel traegt die Person im Pfad — nicht als Geheimnis,
        // sondern damit ein Aufraeumen nach Person moeglich bleibt, ohne die
        // Zeilen zu befragen.
        return new DocumentRecord(
            id, subject, sauber, kind, inhaltstyp, groesse,
            $"{subject.Value:N}/{id:N}", now);
    }

    /// <summary>Die DocumentRecord, wie eine Zeile sie hält. Prüft nichts.</summary>
    /// <remarks>
    /// Der Weg des Speichers zurück. Bewusst ohne die Regeln: eine Zeile, die
    /// unter einer älteren Grenze geschrieben wurde, muss lesbar bleiben —
    /// jemandem seine DocumentRecord zu verbergen, weil sich der Code geändert hat,
    /// wäre die schlechtere Antwort.
    /// </remarks>
    public static DocumentRecord Stelle_her(
        Guid id,
        SubjectId subject,
        string name,
        DocumentKind kind,
        string inhaltstyp,
        int groesse,
        string ablageschluessel,
        DateTimeOffset hochgeladen) =>
        new(id, subject, name, kind, inhaltstyp, groesse, ablageschluessel, hochgeladen);
}

/// <summary>Findet und speichert Unterlagen.</summary>
public interface IDocumentRecordStore
{
    /// <summary>Die Unterlagen dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<DocumentRecord>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Eine bestimmte — oder <c>null</c>.</summary>
    Task<DocumentRecord?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Die genannten, in der Reihenfolge der Kennungen.</summary>
    /// <remarks>
    /// Für die Mappe: sie zeigt genau die Unterlagen, die eine JobApplication
    /// beigelegt hat, und nicht alle, die jemand jemals hochgeladen hat.
    /// </remarks>
    Task<IReadOnlyList<DocumentRecord>> GetManyAsync(
        SubjectId subject, IReadOnlyList<Guid> kennungen, CancellationToken cancellationToken = default);

    /// <summary>Legt an.</summary>
    Task SaveAsync(DocumentRecord document, CancellationToken cancellationToken = default);

    /// <summary>Löscht eine.</summary>
    Task DeleteAsync(SubjectId subject, Guid id, CancellationToken cancellationToken = default);
}
