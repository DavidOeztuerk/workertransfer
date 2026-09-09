using Girder.Core.Identity;

namespace WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

/// <summary>Das Arbeitgeberprofil — wie ein Unternehmen sich zeigt.</summary>
/// <remarks>
/// <c>tenants</c> in identity-service hält Name und Domain: die
/// <strong>Identität</strong>, entstanden aus einem Domain-Nachweis (ADR-0019).
/// Hier geht es um <strong>Darstellung</strong>. Dieselbe Trennung wie zwischen
/// identity-service und profile-service bei einer Person: die eine Seite weiß,
/// wer jemand ist, die andere, wie er sich zeigt.
/// <para>
/// Der Consent-Ledger kommt nicht vor, und das ist kein Versehen: hier ist
/// niemand betroffen, der einwilligen könnte. Ein Unternehmen macht eine
/// Aussage über sich selbst.
/// </para>
/// </remarks>
public sealed class Arbeitgeberprofil
{
    /// <summary>Wie lang der Anzeigename sein darf.</summary>
    public const int HoechstlaengeAnzeigename = 160;

    /// <summary>Wie lang der Selbstbeschreibungstext sein darf.</summary>
    public const int HoechstlaengeUeberUns = 8000;

    /// <summary>Wie lang ein Link sein darf.</summary>
    public const int HoechstlaengeLink = 2000;

    /// <summary>Wie lang ein Listeneintrag sein darf.</summary>
    public const int HoechstlaengeEintrag = 120;

    /// <summary>Wie viele Einträge eine Liste haben darf.</summary>
    public const int HoechsteEintraege = 20;

    private Arbeitgeberprofil(
        TenantId firma,
        string kuerzel,
        Geprueftes werte,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Firma = firma;
        Kuerzel = kuerzel;
        Anzeigename = werte.Anzeigename;
        UeberUns = werte.UeberUns;
        Netzseite = werte.Netzseite;
        Orte = werte.Orte;
        Leistungen = werte.Leistungen;
        Zeile1 = werte.Zeile1;
        Postleitzahl = werte.Postleitzahl;
        Ort = werte.Ort;
        Land = werte.Land;
        Telefon = werte.Telefon;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Welches Unternehmen. Es <em>ist</em> der Schlüssel.</summary>
    public TenantId Firma { get; }

    /// <summary>Die Adresse der Karriere-Seite.</summary>
    /// <remarks>
    /// Einmal vergeben und danach unveränderlich — sie ist ein Versprechen, und
    /// ein Kürzel, das dem Anzeigenamen folgt, bricht jeden geteilten Link.
    /// Deshalb hat diese Klasse keinen Weg, es zu ändern.
    /// </remarks>
    public string Kuerzel { get; }

    /// <summary>Die Marke.</summary>
    /// <remarks>
    /// <strong>Nicht</strong> <c>tenants.name</c>: der eine ist der Kontoname
    /// bei der Anlage, der andere die Marke. Keiner wird aus dem anderen
    /// abgeleitet, also gibt es hier keine Kopie, die driften könnte.
    /// </remarks>
    public string Anzeigename { get; private set; }

    /// <summary>Was das Unternehmen über sich schreibt.</summary>
    public string UeberUns { get; private set; }

    /// <summary><c>null</c>, wenn keine angegeben ist.</summary>
    public string? Netzseite { get; private set; }

    /// <summary>Wo gearbeitet wird.</summary>
    public IReadOnlyList<string> Orte { get; private set; }

    /// <summary>Was geboten wird.</summary>
    public IReadOnlyList<string> Leistungen { get; private set; }

    /// <summary>Straße für den Briefkopf. Leer, wenn nicht hinterlegt.</summary>
    public string Zeile1 { get; private set; }

    public string Postleitzahl { get; private set; }

    public string Ort { get; private set; }

    public string Land { get; private set; }

    public string Telefon { get; private set; }

    /// <summary>Wann es angelegt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann es zuletzt geändert wurde.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Legt ein Profil an.</summary>
    /// <exception cref="Profilfehler">Etwas an der Eingabe stimmt nicht.</exception>
    public static Arbeitgeberprofil Lege_an(
        TenantId firma,
        string kuerzel,
        string anzeigename,
        string ueberUns,
        string? netzseite,
        IReadOnlyList<string>? orte,
        IReadOnlyList<string>? leistungen,
        DateTimeOffset jetzt,
        string? zeile1 = null,
        string? postleitzahl = null,
        string? ort = null,
        string? land = null,
        string? telefon = null) =>
        new(firma, kuerzel,
            Pruefe(anzeigename, ueberUns, netzseite, orte, leistungen,
                zeile1, postleitzahl, ort, land, telefon),
            jetzt, jetzt);

    /// <summary>Das Profil, wie eine Zeile es hält.</summary>
    public static Arbeitgeberprofil Stelle_her(
        TenantId firma,
        string kuerzel,
        string anzeigename,
        string ueberUns,
        string? netzseite,
        IReadOnlyList<string> orte,
        IReadOnlyList<string> leistungen,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        string zeile1 = "",
        string postleitzahl = "",
        string ort = "",
        string land = "DE",
        string telefon = "") =>
        new(firma, kuerzel,
            new Geprueftes(anzeigename, ueberUns, netzseite, orte, leistungen,
                zeile1, postleitzahl, ort, land, telefon),
            angelegtAm, geaendertAm);

    /// <summary>Schreibt die Felder neu — das Kürzel nicht.</summary>
    /// <exception cref="Profilfehler">Etwas an der Eingabe stimmt nicht.</exception>
    public void Aendere(
        string anzeigename,
        string ueberUns,
        string? netzseite,
        IReadOnlyList<string>? orte,
        IReadOnlyList<string>? leistungen,
        DateTimeOffset jetzt,
        string? zeile1 = null,
        string? postleitzahl = null,
        string? ort = null,
        string? land = null,
        string? telefon = null)
    {
        // Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular
        // darf kein halb geändertes Aggregat hinterlassen.
        var geprueft = Pruefe(anzeigename, ueberUns, netzseite, orte, leistungen,
            zeile1, postleitzahl, ort, land, telefon);

        Anzeigename = geprueft.Anzeigename;
        UeberUns = geprueft.UeberUns;
        Netzseite = geprueft.Netzseite;
        Orte = geprueft.Orte;
        Leistungen = geprueft.Leistungen;
        Zeile1 = geprueft.Zeile1;
        Postleitzahl = geprueft.Postleitzahl;
        Ort = geprueft.Ort;
        Land = geprueft.Land;
        Telefon = geprueft.Telefon;
        GeaendertAm = jetzt;
    }

    /// <summary>Die geprüften Werte, getippt.</summary>
    /// <remarks>
    /// Gemeinsam für Anlegen und Ändern, damit die beiden nicht auseinander
    /// laufen können — fünf Felder, die alle Zeichenketten oder Listen davon
    /// sind, sind genau die Verwechslung, die niemand bemerkt.
    /// </remarks>
    private sealed record Geprueftes(
        string Anzeigename,
        string UeberUns,
        string? Netzseite,
        IReadOnlyList<string> Orte,
        IReadOnlyList<string> Leistungen,
        string Zeile1,
        string Postleitzahl,
        string Ort,
        string Land,
        string Telefon);

    private static Geprueftes Pruefe(
        string anzeigename,
        string ueberUns,
        string? netzseite,
        IReadOnlyList<string>? orte,
        IReadOnlyList<string>? leistungen,
        string? zeile1 = null,
        string? postleitzahl = null,
        string? ort = null,
        string? land = null,
        string? telefon = null)
    {
        var iso = Text("country", land, pflicht: false, 2).ToUpperInvariant();
        return new(
            Text("Display name", anzeigename, pflicht: true, HoechstlaengeAnzeigename),
            Text("About", ueberUns, pflicht: false, HoechstlaengeUeberUns),
            Link(netzseite),
            Eintraege("locations", orte),
            Eintraege("benefits", leistungen),
            Text("line1", zeile1, pflicht: false, 120),
            Text("postal_code", postleitzahl, pflicht: false, 16),
            Text("city", ort, pflicht: false, 80),
            iso.Length == 2 ? iso : "DE",
            Text("phone", telefon, pflicht: false, 40));
    }

    private static string Text(string feld, string? wert, bool pflicht, int grenze)
    {
        var bereinigt = (wert ?? string.Empty).Trim();

        if (pflicht && bereinigt.Length == 0)
        {
            throw new Textfehler(feld, "must not be empty");
        }

        return bereinigt.Length > grenze
            ? throw new Textfehler(feld, $"exceeds {grenze} characters")
            : bereinigt;
    }

    private static string? Link(string? wert)
    {
        var bereinigt = (wert ?? string.Empty).Trim();

        if (bereinigt.Length == 0)
        {
            // Leer und „nicht angegeben" sind dasselbe; eine leere Zeichenkette
            // würde als Link dargestellt und führte ins Nichts.
            return null;
        }

        if (bereinigt.Length > HoechstlaengeLink)
        {
            throw new Linkfehler($"The link exceeds {HoechstlaengeLink} characters");
        }

        if (!Uri.TryCreate(bereinigt, UriKind.Absolute, out var adresse))
        {
            throw new Linkfehler("The link is missing a host");
        }

        if (adresse.Scheme != Uri.UriSchemeHttp && adresse.Scheme != Uri.UriSchemeHttps)
        {
            throw new Linkfehler("Only http and https links are allowed");
        }

        return string.IsNullOrEmpty(adresse.Host)
            ? throw new Linkfehler("The link is missing a host")
            : bereinigt;
    }

    /// <summary>Getrimmt, ohne Leeres, ohne Dubletten — die Reihenfolge bleibt.</summary>
    /// <remarks>
    /// Erst entdoppeln, dann zählen: sonst würde jemand mit einundzwanzigmal
    /// „Homeoffice" abgewiesen, obwohl daraus ein Eintrag wird.
    /// </remarks>
    private static IReadOnlyList<string> Eintraege(string feld, IReadOnlyList<string>? roh)
    {
        if (roh is null)
        {
            return [];
        }

        var behalten = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var eintrag in roh)
        {
            var bereinigt = (eintrag ?? string.Empty).Trim();

            if (bereinigt.Length == 0)
            {
                continue;
            }

            if (bereinigt.Length > HoechstlaengeEintrag)
            {
                throw new Textfehler(feld, $"entry exceeds {HoechstlaengeEintrag} characters");
            }

            if (gesehen.Add(bereinigt))
            {
                behalten.Add(bereinigt);
            }
        }

        return behalten.Count > HoechsteEintraege
            ? throw new ZuVieleEintraege(feld, HoechsteEintraege)
            : behalten;
    }
}

/// <summary>Findet und speichert Arbeitgeberprofile.</summary>
public interface IProfilspeicher
{
    /// <summary>Das Profil dieses Unternehmens, oder <c>null</c>.</summary>
    Task<Arbeitgeberprofil?> HoleAsync(
        TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Das Profil hinter diesem Kürzel, oder <c>null</c>.</summary>
    Task<Arbeitgeberprofil?> HoleAsync(string kuerzel, CancellationToken cancellationToken = default);

    /// <summary>Das gewünschte Kürzel, oder das nächste freie mit Zähler.</summary>
    /// <remarks>
    /// <strong>Nicht die Mandanten-Kennung anhängen</strong>: die stünde dann in
    /// einer Adresse, die weitergegeben wird.
    /// </remarks>
    Task<string> FreiesKuerzelAsync(string gewuenscht, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Arbeitgeberprofil profil, CancellationToken cancellationToken = default);
}
