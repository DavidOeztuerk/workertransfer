using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>
/// Die PostalAddress einer Person — ResumeTemplate für Briefkopf und Arbeitgebermappe.
/// </summary>
/// <remarks>
/// <strong>Zweckbindung (ADR-0038).</strong> Straße und Telefon gehören auf den
/// Brief, nicht in die Search, nicht ins Token, nicht an ein Modell. Wer nie
/// eine PostalAddress hinterlegt, hat keine Zeile — Privacy by Default, Art. 25.
/// <para>
/// Eine SubjectKeyedTable: der Schlüssel IST die Person. Der Löschwächter findet
/// die Tabelle über die Anmerkung, nicht über eine <c>subject_id</c>-Spalte
/// daneben.
/// </para>
/// </remarks>
public sealed class PostalAddress
{
    private PostalAddress(SubjectId subject)
    {
        Wer = subject;
    }

    /// <summary>Wessen. Zugleich der Schlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Straße und Hausnummer.</summary>
    public string Zeile1 { get; private set; } = string.Empty;

    /// <summary>Adresszusatz, oder leer.</summary>
    public string Zeile2 { get; private set; } = string.Empty;

    /// <summary>Postleitzahl.</summary>
    public string Postleitzahl { get; private set; } = string.Empty;

    /// <summary>Ort.</summary>
    public string Ort { get; private set; } = string.Empty;

    /// <summary>ISO-3166-1 alpha-2, Vorgabe DE.</summary>
    public string Land { get; private set; } = "DE";

    /// <summary>Telefon, optional.</summary>
    public string Telefon { get; private set; } = string.Empty;

    /// <summary>Noch nichts hinterlegt.</summary>
    public static PostalAddress Leer(SubjectId subject) => new(subject);

    /// <summary>Wie die Zeile sie hält.</summary>
    public static PostalAddress Wiederherstellen(
        SubjectId subject,
        string zeile1,
        string zeile2,
        string postleitzahl,
        string ort,
        string land,
        string telefon)
    {
        var anschrift = new PostalAddress(subject);
        anschrift.Set(zeile1, zeile2, postleitzahl, ort, land, telefon);
        return anschrift;
    }

    /// <summary>Schreibt die ResumeTemplate. Leere Felder bleiben leer — kein Zwang.</summary>
    public void Set(
        string? zeile1,
        string? zeile2,
        string? postleitzahl,
        string? ort,
        string? land,
        string? telefon)
    {
        Zeile1 = Kuerze(zeile1, 120);
        Zeile2 = Kuerze(zeile2, 120);
        Postleitzahl = Kuerze(postleitzahl, 16);
        Ort = Kuerze(ort, 80);
        var iso = Kuerze(land, 2).ToUpperInvariant();
        Land = iso.Length == 2 ? iso : "DE";
        Telefon = Kuerze(telefon, 40);
    }

    private static string Kuerze(string? value, int hoechstens)
    {
        var getrimmt = (value ?? string.Empty).Trim();
        return getrimmt.Length <= hoechstens ? getrimmt : getrimmt[..hoechstens];
    }
}
