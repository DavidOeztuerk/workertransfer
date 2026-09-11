using WorkerTransfer.Skills;

namespace WorkerTransfer.Scout.Domain.Suchen;

/// <summary>Eine Eingabe, die dieser Dienst nicht annehmen kann.</summary>
/// <remarks>
/// Die Meldung nennt die <em>Regel</em>, nie den Wert. Sie wird protokolliert
/// (Girders <c>ValidationBehavior</c> tut das), und ein Suchbegriff ist die
/// Eingabe eines Menschen über einen anderen.
/// </remarks>
/// <param name="grund">Welche Regel verletzt wurde.</param>
public class Eingabefehler(string grund) : Exception(grund);

/// <summary>Wonach ein Unternehmen sucht — und zwar ausschliesslich nach Genanntem.</summary>
/// <remarks>
/// <para><strong>Hier steht die dritte Auflage aus ADR-0036 als Typ.</strong>
/// Ein Filter trägt nur Worte, die eine Person selbst in ihr Profil getippt hat
/// (Herkunft <em>genannt</em>, ADR-0033). Belege — GitHub-Topics, Sprachen,
/// Technologien an einer Station — werden zum Treffer <em>dazugeholt</em> und
/// sind hier mit Absicht nicht vorgesehen: ein Beleg ist eine Aussage über ein
/// Artefakt, und wer danach suchte, machte sie stillschweigend zu einer über
/// den Menschen.</para>
///
/// <para><strong>Die Worte sind ein ODER, kein UND</strong>, und das ist keine
/// Lockerung: unter UND nennt jeder Treffer alles Gesuchte, jedes Häkchen wäre
/// gesetzt, und „welche Fähigkeit fehlt" hätte keine Antwort (ADR-0036
/// Entscheidung 2). Wer alles verlangt, liest es an den Häkchen ab — und
/// entscheidet selbst, statt eine Zahl zu bekommen, die für ihn entscheidet.</para>
///
/// <para>Erst kanonisieren, dann entdoppeln (ADR-0023, die Reihenfolge trägt):
/// andersherum stünden „Postgres" und „PostgreSQL" zweimal in der Häkchenliste,
/// und derselbe Mensch hätte für dieselbe Fähigkeit zwei Haken.</para>
/// </remarks>
public sealed class Suchfilter
{
    /// <summary>Wie viele Fähigkeiten eine Suche nennen darf.</summary>
    /// <remarks>
    /// Dieselbe Überlegung wie beim Seitendeckel: ohne Grenze baut ein Aufrufer
    /// mit einer einzigen Adresszeile eine beliebig teure Abfrage — jede
    /// Fähigkeit ist eine weitere Bedingung auf einer fremden Datenbank.
    /// </remarks>
    public const int HoechstzahlWorte = 10;

    /// <summary>Wie lang ein einzelnes Wort sein darf.</summary>
    public const int HoechstlaengeWort = Faehigkeitsgrenzen.Hoechstlaenge;

    /// <summary>Wie lang der Ort sein darf.</summary>
    public const int HoechstlaengeOrt = 120;

    /// <summary>Der leere Filter — „zeig mir, wer sich zeigen will".</summary>
    /// <remarks>
    /// Ausdrücklich erlaubt. Eine Suche ohne Wort ist keine leere Frage: sie
    /// nennt genau niemanden und zeigt trotzdem nur, wer freigegeben hat.
    /// </remarks>
    public static Suchfilter Leer { get; } = new([], string.Empty, false);

    private Suchfilter(IReadOnlyList<string> genannteWorte, string ort, bool nurRemote)
    {
        GenannteWorte = genannteWorte;
        Ort = ort;
        NurRemote = nurRemote;
    }

    /// <summary>Die gesuchten Worte, kanonisch und entdoppelt.</summary>
    public IReadOnlyList<string> GenannteWorte { get; }

    /// <summary>Teiltext auf dem Ort, oder leer.</summary>
    public string Ort { get; }

    /// <summary>
    /// Nur, wer „Remote möglich" ausdrücklich angekreuzt hat.
    /// </summary>
    /// <remarks>
    /// Nur in eine Richtung. <c>false</c> heisst „nicht ja gesagt", nicht
    /// „lehne ab" — ein Filter auf das Gegenteil schlösse aus, wer schlicht
    /// nichts angekreuzt hat.
    /// </remarks>
    public bool NurRemote { get; }

    /// <summary>Baut den Filter aus dem, was jemand getippt hat.</summary>
    /// <exception cref="Eingabefehler">Zu viele Worte, ein zu langes, ein zu langer Ort.</exception>
    public static Suchfilter Aus(IEnumerable<string>? worte, string? ort, bool nurRemote)
    {
        var getippt = (worte ?? [])
            .Select(wort => wort?.Trim() ?? string.Empty)
            .Where(wort => wort.Length > 0)
            .ToArray();

        if (getippt.Any(wort => wort.Length > HoechstlaengeWort))
        {
            throw new Eingabefehler(
                $"a search word may not be longer than {HoechstlaengeWort} characters");
        }

        // Kanonisieren zuerst, entdoppeln danach (ADR-0023).
        var kanonisch = Wortschatz.KanonischAlle(getippt)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (kanonisch.Length > HoechstzahlWorte)
        {
            throw new Eingabefehler(
                $"a search may name at most {HoechstzahlWorte} skills");
        }

        var ortWert = (ort ?? string.Empty).Trim();

        return ortWert.Length > HoechstlaengeOrt
            ? throw new Eingabefehler(
                $"a location may not be longer than {HoechstlaengeOrt} characters")
            : new Suchfilter(kanonisch, ortWert, nurRemote);
    }

    /// <summary>Den Filter, wie eine gespeicherte Zeile ihn hält.</summary>
    /// <remarks>
    /// Ohne Prüfung der Grenzen. Eine gespeicherte Suche war bei ihrer
    /// Entstehung gültig; sie beim Lesen abzulehnen hiesse, jemandem seine
    /// gespeicherte Suche zu entziehen, weil später eine Obergrenze sank.
    /// Kanonisiert wird trotzdem — ein neuer Eintrag im Wortschatz wirkt so
    /// auch auf ältere Zeilen, ohne Datenwanderung.
    /// </remarks>
    public static Suchfilter Stelle_her(IEnumerable<string>? worte, string? ort, bool nurRemote) =>
        new(
            [.. Wortschatz.KanonischAlle(worte ?? []).Distinct(StringComparer.OrdinalIgnoreCase)],
            (ort ?? string.Empty).Trim(),
            nurRemote);
}
