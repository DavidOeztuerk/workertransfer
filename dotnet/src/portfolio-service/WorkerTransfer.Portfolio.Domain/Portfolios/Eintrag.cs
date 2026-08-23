using System.Text.RegularExpressions;

namespace WorkerTransfer.Portfolio.Domain.Portfolios;

/// <summary>Ein Stück Arbeit, das jemand gezeigt haben will.</summary>
/// <remarks>
/// Trägt <b>keine Sichtbarkeitsstufe</b>. Ein Portfolio ist ein Schaufenster,
/// kein Aktenschrank: freigegeben wird es als Ganzes, und die Feinheit steckt
/// in der Entscheidung, was hineinkommt. Was nicht gezeigt werden darf, gehört
/// nicht hinein — eine Stufe je Eintrag wäre eine zweite Wahrheit neben dem
/// Ledger (ADR-0020 §6).
/// </remarks>
public sealed partial record Eintrag
{
    /// <summary>Wie lang eine Überschrift sein darf.</summary>
    public const int HoechstlaengeTitel = 160;

    /// <summary>Wie lang eine Zusammenfassung sein darf.</summary>
    public const int HoechstlaengeZusammenfassung = 1000;

    /// <summary>Wie lang eine Rollenangabe sein darf.</summary>
    public const int HoechstlaengeRolle = 160;

    /// <summary>Wie lang ein Link sein darf.</summary>
    public const int HoechstlaengeLink = 2000;

    /// <summary>Wie lang ein Anhangname sein darf.</summary>
    public const int HoechstlaengeAnhang = 80;

    /// <summary>Vor diesem Jahr hat niemand hier etwas veröffentlicht.</summary>
    public const int FruehestesJahr = 1900;

    /// <summary>
    /// Nur diese beiden Schemata.
    /// </summary>
    /// <remarks>
    /// Der Link wird von fremden Menschen angeklickt, und das Feld landet in
    /// einem Browser. Eine Liste erlaubter Schemata ist hier kein Purismus,
    /// sondern die Stelle, an der ein <c>javascript:</c> abgefangen wird.
    /// </remarks>
    public static IReadOnlySet<string> ErlaubteSchemata { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "http", "https" };

    private Eintrag(
        string titel, string zusammenfassung, string? link, string rolle, int? jahr, string? anhang)
    {
        Titel = titel;
        Zusammenfassung = zusammenfassung;
        Link = link;
        Rolle = rolle;
        Jahr = jahr;
        Anhang = anhang;
    }

    /// <summary>Worum es geht.</summary>
    public string Titel { get; }

    /// <summary>Was die Person selbst darüber geschrieben hat.</summary>
    public string Zusammenfassung { get; }

    /// <summary><c>null</c> heißt: kein Link.</summary>
    public string? Link { get; }

    /// <summary>Was sie dabei getan hat.</summary>
    public string Rolle { get; }

    /// <summary><c>null</c> heißt: kein Jahr genannt.</summary>
    public int? Jahr { get; }

    /// <summary>
    /// Der Name einer hochgeladenen Datei, vom Server vergeben.
    /// </summary>
    /// <remarks>
    /// Kein Pfad und keine URL: er wird beim Ausliefern mit der
    /// <c>subject_id</c> zu einem Ablageschlüssel zusammengesetzt, und genau
    /// diese Struktur verhindert, dass jemand mit einem fremden Namen an eine
    /// fremde Datei kommt.
    /// </remarks>
    public string? Anhang { get; }

    /// <summary>Baut einen Eintrag, oder weist ihn ab.</summary>
    /// <param name="jetzt">
    /// Für die Obergrenze des Jahres. Hineingereicht, statt sich eine Uhr zu
    /// holen: ein Wertobjekt mit versteckter Gegenwart ist in einem Test nicht
    /// festzunageln.
    /// </param>
    public static Eintrag Aus(
        string titel,
        DateTimeOffset jetzt,
        string zusammenfassung = "",
        string? link = null,
        string rolle = "",
        int? jahr = null,
        string? anhang = null) =>
        new(
            Text("Der Titel", titel, pflicht: true, HoechstlaengeTitel),
            Text("Die Zusammenfassung", zusammenfassung, pflicht: false, HoechstlaengeZusammenfassung),
            Geprueft(link),
            Text("Die Rolle", rolle, pflicht: false, HoechstlaengeRolle),
            Jahreszahl(jahr, jetzt),
            Anhangname(anhang));

    private static string Text(string feld, string? wert, bool pflicht, int grenze)
    {
        var bereinigt = (wert ?? string.Empty).Trim();

        if (pflicht && bereinigt.Length == 0)
        {
            throw new TextFehler(feld, "darf nicht leer sein");
        }

        return bereinigt.Length > grenze
            ? throw new TextFehler(feld, $"ist länger als {grenze} Zeichen")
            : bereinigt;
    }

    private static string? Geprueft(string? link)
    {
        var bereinigt = link?.Trim();

        // Leer und „nicht angegeben" sind dasselbe. Ein leerer String würde
        // später als Link gerendert und führte ins Nichts.
        if (string.IsNullOrEmpty(bereinigt))
        {
            return null;
        }

        if (bereinigt.Length > HoechstlaengeLink)
        {
            throw new LinkFehler($"Der Link ist länger als {HoechstlaengeLink} Zeichen.");
        }

        if (!Uri.TryCreate(bereinigt, UriKind.Absolute, out var zerlegt))
        {
            throw new LinkFehler("Das ist keine vollständige Adresse.");
        }

        if (!ErlaubteSchemata.Contains(zerlegt.Scheme))
        {
            throw new LinkFehler("Nur http- und https-Links sind erlaubt.");
        }

        return string.IsNullOrEmpty(zerlegt.Host)
            ? throw new LinkFehler("Dem Link fehlt der Rechnername.")
            : bereinigt;
    }

    private static int? Jahreszahl(int? jahr, DateTimeOffset jetzt)
    {
        if (jahr is not { } wert)
        {
            return null;
        }

        // Das nächste Jahr ist erlaubt: etwas kann gerade erscheinen. Weiter in
        // die Zukunft ist ein Tippfehler, kein Plan.
        return wert < FruehestesJahr || wert > jetzt.Year + 1
            ? throw new JahrFehler(
                $"Ein Jahr muss zwischen {FruehestesJahr} und {jetzt.Year + 1} liegen.")
            : wert;
    }

    /// <summary>
    /// Nur ein Name, kein Pfad.
    /// </summary>
    /// <remarks>
    /// Der Client bekommt ihn vom Hochladen zurück und schickt ihn beim
    /// Speichern mit. Ließe man einen Pfad zu, könnte jemand mit <c>../</c> aus
    /// seinem eigenen Verzeichnis herauszeigen — die Ablage fängt das zwar auch
    /// ab, aber eine Prüfung an der Grenze ist billiger als eine Ausnahme in
    /// der Tiefe.
    /// </remarks>
    private static string? Anhangname(string? anhang)
    {
        var bereinigt = anhang?.Trim();

        if (string.IsNullOrEmpty(bereinigt))
        {
            return null;
        }

        return bereinigt.Length > HoechstlaengeAnhang || !Anhangmuster().IsMatch(bereinigt)
            ? throw new AnhangFehler()
            : bereinigt;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex Anhangmuster();
}
