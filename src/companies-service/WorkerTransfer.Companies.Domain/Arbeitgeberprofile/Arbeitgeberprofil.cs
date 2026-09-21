using Noelia.Core.Identity;

namespace WorkerTransfer.Companies.Domain.EmployerProfiles;

/// <summary>Das EmployerProfile — wie ein Unternehmen sich zeigt.</summary>
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
public sealed class EmployerProfile
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

    private EmployerProfile(
        TenantId company,
        string slug,
        Geprueftes values,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Firma = company;
        Slug = slug;
        Anzeigename = values.Anzeigename;
        UeberUns = values.UeberUns;
        Netzseite = values.Netzseite;
        Orte = values.Orte;
        Leistungen = values.Leistungen;
        Zeile1 = values.Zeile1;
        Postleitzahl = values.Postleitzahl;
        Ort = values.Ort;
        Land = values.Land;
        Telefon = values.Telefon;
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
    public string Slug { get; }

    /// <summary>Die Marke.</summary>
    /// <remarks>
    /// <strong>Nicht</strong> <c>tenants.name</c>: der eine ist der Kontoname
    /// bei der Anlage, der andere die Marke. None wird aus dem anderen
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

    /// <summary>Legt ein Profile an.</summary>
    /// <exception cref="Profilfehler">Etwas an der Eingabe stimmt nicht.</exception>
    public static EmployerProfile Lege_an(
        TenantId company,
        string slug,
        string anzeigename,
        string ueberUns,
        string? netzseite,
        IReadOnlyList<string>? orte,
        IReadOnlyList<string>? leistungen,
        DateTimeOffset now,
        string? zeile1 = null,
        string? postleitzahl = null,
        string? ort = null,
        string? land = null,
        string? telefon = null) =>
        new(company, slug,
            Check(anzeigename, ueberUns, netzseite, orte, leistungen,
                zeile1, postleitzahl, ort, land, telefon),
            now, now);

    /// <summary>Das Profile, wie eine Zeile es hält.</summary>
    public static EmployerProfile Stelle_her(
        TenantId company,
        string slug,
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
        new(company, slug,
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
        DateTimeOffset now,
        string? zeile1 = null,
        string? postleitzahl = null,
        string? ort = null,
        string? land = null,
        string? telefon = null)
    {
        // Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular
        // darf kein halb geändertes Aggregat hinterlassen.
        var geprueft = Check(anzeigename, ueberUns, netzseite, orte, leistungen,
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
        GeaendertAm = now;
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

    private static Geprueftes Check(
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

    private static string Text(string field, string? value, bool pflicht, int limit)
    {
        var bereinigt = (value ?? string.Empty).Trim();

        if (pflicht && bereinigt.Length == 0)
        {
            throw new Textfehler(field, "must not be empty");
        }

        return bereinigt.Length > limit
            ? throw new Textfehler(field, $"exceeds {limit} characters")
            : bereinigt;
    }

    private static string? Link(string? value)
    {
        var bereinigt = (value ?? string.Empty).Trim();

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

        if (!Uri.TryCreate(bereinigt, UriKind.Absolute, out var address))
        {
            throw new Linkfehler("The link is missing a host");
        }

        if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
        {
            throw new Linkfehler("Only http and https links are allowed");
        }

        return string.IsNullOrEmpty(address.Host)
            ? throw new Linkfehler("The link is missing a host")
            : bereinigt;
    }

    /// <summary>Getrimmt, ohne Leeres, ohne Dubletten — die Reihenfolge bleibt.</summary>
    /// <remarks>
    /// Erst entdoppeln, dann zählen: sonst würde jemand mit einundzwanzigmal
    /// „Homeoffice" abgewiesen, obwohl daraus ein Eintrag wird.
    /// </remarks>
    private static IReadOnlyList<string> Eintraege(string field, IReadOnlyList<string>? roh)
    {
        if (roh is null)
        {
            return [];
        }

        var behalten = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in roh)
        {
            var bereinigt = (entry ?? string.Empty).Trim();

            if (bereinigt.Length == 0)
            {
                continue;
            }

            if (bereinigt.Length > HoechstlaengeEintrag)
            {
                throw new Textfehler(field, $"entry exceeds {HoechstlaengeEintrag} characters");
            }

            if (gesehen.Add(bereinigt))
            {
                behalten.Add(bereinigt);
            }
        }

        return behalten.Count > HoechsteEintraege
            ? throw new ZuVieleEintraege(field, HoechsteEintraege)
            : behalten;
    }
}

/// <summary>Findet und speichert EmployerProfiles.</summary>
public interface IProfileStore
{
    /// <summary>Das Profile dieses Unternehmens, oder <c>null</c>.</summary>
    Task<EmployerProfile?> GetAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Das Profile hinter diesem Kürzel, oder <c>null</c>.</summary>
    Task<EmployerProfile?> GetAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Das gewünschte Kürzel, oder das nächste freie mit Zähler.</summary>
    /// <remarks>
    /// <strong>Nicht die Mandanten-Kennung anhängen</strong>: die stünde dann in
    /// einer Adresse, die weitergegeben wird.
    /// </remarks>
    Task<string> FreeSlugAsync(string gewuenscht, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(EmployerProfile profile, CancellationToken cancellationToken = default);
}
