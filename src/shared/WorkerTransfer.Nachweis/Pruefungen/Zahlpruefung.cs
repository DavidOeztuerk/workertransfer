using System.Reflection;
using System.Text.RegularExpressions;

namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Ein Name, bei dem ein Wort der Verbotsliste ein Homonym ist.</summary>
/// <remarks>
/// <para><strong>Je Name, nie je Dienst und nie je Wort.</strong> Eine Ausnahme
/// für ein <em>Wort</em> nähme die Prüfung an dieser Stelle ganz aus dem
/// Betrieb; eine Ausnahme für einen <em>Dienst</em> wäre die Stelle, an der beim
/// nächsten Feld niemand mehr hinsieht. Ein Name ist eine einzelne Entscheidung,
/// und genau eine wird hier getroffen.</para>
///
/// <para><strong>Und sie steht im Befund.</strong> Das ist der eigentliche
/// Mechanismus: wer eine Ausnahme hinzufügt, schreibt ihren Grund in ein
/// Dokument, das ein Betriebsrat liest. Eine stille Liste in einem Testprojekt
/// wächst; eine, die gelesen wird, nicht.</para>
///
/// <para>Dieselbe Form halten die drei Sprachkataloge der Oberfläche für echte
/// Kognaten („Status“, „Website“, „Administrator“): kurz, und jede Zeile ein
/// Einzelfall.</para>
/// </remarks>
/// <param name="Name">Der volle Name, wie die Prüfung ihn bildet: <c>Typ.Glied</c> oder <c>Typ</c>.</param>
/// <param name="Grund">Warum dieses Wort hier kein Urteil über einen Menschen ist.</param>
public sealed record Wortausnahme(string Name, string Grund);

/// <summary>Trägt irgendeine öffentliche Fläche eine Zahl über einen Menschen?</summary>
/// <remarks>
/// <para><strong>Der Laufzeit-Zwilling von <c>Adr0022Tests</c> und
/// <c>AuflagenTests</c>.</strong> Er liest Domäne und Verträge des Dienstes, der
/// gerade läuft, und stellt die Frage, die ADR-0022 §1 stellt: gibt es hier
/// einen Namen, der eine Zahl über einen Menschen ankündigt?</para>
///
/// <para><strong>Warum das im Nachweis steht und nicht nur im Test.</strong> Der
/// Mensch, der die Anhang-III-Frage beantwortet, fragt als Erstes, ob dieses
/// System Bewerber in einer Zahl zusammenfasst. „In Domäne und Verträgen von
/// profile-service trägt kein Name eines der Worte“ ist ein Beleg, den er
/// zitieren kann, samt Datum. Ein grüner Test in einem Testprojekt ist keiner —
/// er steht in keinem Dokument.</para>
///
/// <para><strong>Verglichen wird je Silbe und am Silbenanfang — nicht als
/// Teilzeichenkette.</strong> Das ist gemessen und nicht gewählt: eine
/// Teilzeichenkettensuche meldete über diesen Baum <c>Capability</c> (der
/// Kerntyp des Ledgers) wegen <c>ability</c>, <c>Benefits</c> wegen <c>fit</c>
/// und <c>Availability</c> wegen <c>ability</c> — drei Fehlalarme über
/// vollkommen korrekten Code. Genau diesen Fehler hatte Girder 4.3.0 in seiner
/// Maskierung, und 4.4.0 hat ihn mit derselben Bewegung behoben.</para>
///
/// <para><strong>Was der Silbenanfang nicht findet, steht hier, damit niemand
/// mehr glaubt:</strong> ein deutsches Kompositum, das das Wort am
/// <em>Ende</em> trägt, entgeht ihm — <c>Trefferanzahl</c> ist eine Silbe und
/// beginnt nicht mit <c>anzahl</c>. Die Regel ist dadurch nicht vollständig, und
/// sie war es nie: der Name ist das Signal, nicht die Regel. Ein Feld, das eine
/// Zahl über einen Menschen trägt und unauffällig heißt, findet keine
/// Wortliste — dafür gibt es die geschlossenen Feldmengen.</para>
///
/// <para><strong>Geprüft werden Typnamen und Eigenschaften, sonst nichts.</strong>
/// Nicht <c>Equals</c>, nicht <c>Deconstruct</c>, nicht die Operatoren: was der
/// Übersetzer an einen Record hängt, trägt den Typnamen und meldete ihn ein
/// Dutzend Mal. Und nicht die Kommentare — „ohne Bewertung“ ist eine Zusage und
/// soll dort stehen dürfen.</para>
/// </remarks>
/// <param name="flaechen">Domäne und Verträge dieses Dienstes.</param>
/// <param name="ausnahmen">Namen, bei denen ein Wort ein echtes Homonym ist.</param>
/// <param name="wortschatz">
/// Die Worte. Vorgabe ist <see cref="Kern"/>; ein Dienst darf strenger sein —
/// scout-service verbietet in seinen eigenen Auflagen zusätzlich <c>bewert</c>,
/// wo ein Wort neben einem Namen stünde.
/// </param>
public sealed class Zahlpruefung(
    IReadOnlyList<Assembly> flaechen,
    IReadOnlyList<Wortausnahme>? ausnahmen = null,
    IReadOnlyList<string>? wortschatz = null) : IPruefung
{
    /// <summary>Worte, die eine Zahl oder eine Rangfolge über einen Menschen ankündigen.</summary>
    /// <remarks>
    /// <para><strong>Über Zahlen, nicht über Urteile — und das ist eine
    /// Messung.</strong> <c>bewert</c> stand hier und ist gefallen:
    /// assessment-service hält <em>genau eine</em> Bewertung je Vorgang, als
    /// Text, und die Person liest sie immer (ADR-0042). Das Wort platformweit zu
    /// verbieten hieße, dreißig Namen einzeln zu entschuldigen, die alle
    /// richtig heißen — und nach der dritten Entschuldigung liest die Liste
    /// niemand mehr. scout-service verbietet es weiterhin bei sich, und dort
    /// gehört es hin: eine Bewertung neben einem Suchtreffer wäre die Zahl,
    /// die ADR-0022 §1 meint.</para>
    ///
    /// <para><c>ability</c> und <c>talent</c> stehen ebenfalls nicht hier.
    /// <c>Capability</c> ist der Kerntyp des Ledgers, und was ein Mensch kann,
    /// heißt in diesem Baum <c>Faehigkeit</c> — eine <em>Nennung</em> der
    /// Person und ausdrücklich erlaubt (ADR-0023). Ein Wort, das nur Fehlalarme
    /// erzeugt, schwächt die ganze Liste.</para>
    ///
    /// <para><c>note</c> bleibt, obwohl es sieben Ausnahmen kostet: das deutsche
    /// Wort ist die gefährlichste Vokabel, die eine Vermittlungsplattform haben
    /// kann, und ADR-0042 baut den ganzen Dienst um sein Verbot herum. Dass es
    /// auf Englisch „Anmerkung“ heißt, ist der Preis — und jede der sieben
    /// Ausnahmen sagt das in einem Satz, den jemand liest.</para>
    /// </remarks>
    public static IReadOnlyList<string> Kern { get; } =
    [
        "score", "punkt", "rank", "rang", "passung", "fit", "percent", "prozent",
        "quote", "anzahl", "gewicht", "weight", "level", "note", "match",
        "probability", "wahrscheinlich", "aktivitaet", "activity"
    ];

    /// <summary>Die Worte, die diese Prüfung fährt.</summary>
    public IReadOnlyList<string> Verboten { get; } = wortschatz ?? Kern;

    /// <inheritdoc />
    public string Id => "wt.ki.keine-zahl";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.KI;

    /// <inheritdoc />
    /// <remarks>
    /// „Keine Zahl über einen Menschen“ ist die Tatsache, die Art. 22 DSGVO am
    /// nächsten kommt, ohne ihn zu beantworten — und sie ist das Erste, wonach
    /// gefragt wird, wenn jemand den Einsatz nach Anhang III einstuft.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege =>
    [
        Rechtsbezuege.Einzelentscheidung,
        Rechtsbezuege.AnhangIII,
        Rechtsbezuege.Mitbestimmung
    ];

    /// <inheritdoc />
    public Task<Befund> LaufenAsync(CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(flaechen);

        var namen = Namen();

        if (namen.Count == 0)
        {
            // Sonst meldete die Pruefung gruen ueber nichts — derselbe Fehler,
            // den `scripts/test-dotnet.sh` mit seiner Zahl auf dem Schirm
            // schliesst.
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.Fehlt,
                "Die abgesuchten Flächen sind leer — diese Prüfung hat nichts "
                + "angesehen und belegt damit nichts.",
                "Im Verbundpunkt des Dienstes nachsehen, welche Assemblies "
                + "übergeben werden."));
        }

        var erlaubt = ausnahmen ?? [];

        // Eine Ausnahme fuer einen Namen, den es nicht mehr gibt, ist ein
        // Freibrief, den niemand mehr braucht und den niemand zurueckzieht.
        var verwaist = erlaubt
            .Where(ausnahme => !namen.Contains(ausnahme.Name))
            .Select(ausnahme => ausnahme.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (verwaist.Count > 0)
        {
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.Fehlt,
                $"{Zielkunde.Zahl(verwaist.Count)} Ausnahme(n) zeigen auf Namen, "
                + $"die es nicht mehr gibt: {string.Join(", ", verwaist)}.",
                "Eine Ausnahme ohne Namen ist ein Freibrief, den niemand mehr "
                + "braucht — sie gehört gelöscht, nicht stehengelassen."));
        }

        var treffer = namen
            .Where(name => !erlaubt.Any(
                ausnahme => ausnahme.Name.Equals(name, StringComparison.Ordinal)))
            .SelectMany(name => Verboten
                .Where(wort => Trifft(name, wort))
                .Select(wort => $"{name} ({wort})"))
            .OrderBy(zeile => zeile, StringComparer.Ordinal)
            .ToList();

        if (treffer.Count > 0)
        {
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.Fehlt,
                $"{Zielkunde.Zahl(treffer.Count)} öffentliche(r) Name(n) kündigen "
                + $"eine Zahl über einen Menschen an: {string.Join(", ", treffer)}.",
                "ADR-0022 §1: eine Zahl, die einen Menschen zusammenfasst, samt "
                + "jeder Rangfolge daraus, gibt es hier nicht. Entweder das Feld "
                + "fällt, oder es benennt eine Sache statt einer Person — die "
                + "Seitenlänge heißt in scout-service aus genau diesem Grund "
                + "Seitenlaenge. Ein echtes Homonym wird einzeln begründet und "
                + "steht dann in diesem Befund."));
        }

        return Task.FromResult(new Befund(
            Id,
            Bereich,
            Stand.Erfuellt,
            $"In {Zielkunde.Zahl(flaechen.Count)} Flächen "
            + $"({string.Join(", ", flaechen.Select(Kurzname))}) kündigt keiner "
            + $"von {Zielkunde.Zahl(namen.Count)} öffentlichen Namen eine Zahl "
            + $"über einen Menschen an ({Zielkunde.Zahl(Verboten.Count)} Worte "
            + "geprüft)"
            + (erlaubt.Count > 0
                ? ". Einzeln begründete Homonyme: "
                  + string.Join("; ", erlaubt
                      .OrderBy(ausnahme => ausnahme.Name, StringComparer.Ordinal)
                      .Select(ausnahme => $"{ausnahme.Name} — {ausnahme.Grund}"))
                  + "."
                : "."),
            "Ein Feld, dessen Name eines dieser Worte am Silbenanfang trägt, "
            + "stößt diesen Befund um — und ein Feld, das eine Zahl über einen "
            + "Menschen trägt und unauffällig heißt, ebenfalls: der Name ist das "
            + "Signal, nicht die Regel. Dafür stehen daneben die geschlossenen "
            + "Feldmengen (wt.ki.naht)."));
    }

    /// <summary>Jeder öffentliche Typname und jede öffentliche Eigenschaft.</summary>
    private HashSet<string> Namen() =>
    [
        .. flaechen
            .SelectMany(flaeche => flaeche.GetExportedTypes())
            .SelectMany(typ => typ
                .GetProperties(BindingFlags.Public | BindingFlags.Instance
                               | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(eigenschaft => $"{typ.Name}.{eigenschaft.Name}")
                .Append(typ.Name))
    ];

    /// <summary>Ob ein Name eines der Kernworte am Anfang einer Silbe trägt.</summary>
    /// <remarks>
    /// <para>Öffentlich, weil sie zwei Leser hat: diese Prüfung und der
    /// Bauzeit-Zwilling, der sie auf Einzelfälle loslässt. Eine nachgebaute
    /// zweite Regel wäre die, die als Erste falsch wird — und dann wäre grün
    /// über der einen und rot über der anderen, ohne dass sich etwas geändert
    /// hätte.</para>
    ///
    /// <para>Geprüft wird jede Silbe des Typnamens <em>und</em> jede Silbe des
    /// Gliednamens. Beide, weil ein Typ <c>Passung</c> mit einem Glied
    /// <c>Text</c> genauso falsch ist wie ein Typ <c>Gespraech</c> mit einem
    /// Glied <c>Passung</c>.</para>
    /// </remarks>
    /// <param name="name">Ein Typname oder <c>Typ.Glied</c>.</param>
    /// <returns>Ob eines der Kernworte anschlägt.</returns>
    public static bool Trifft(string name) =>
        Kern.Any(wort => Trifft(name, wort));

    private static bool Trifft(string name, string wort) =>
        name.Split('.')
            .SelectMany(Silben)
            .Any(silbe => silbe.StartsWith(wort, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ein Bezeichner, an seinen Großbuchstaben zerlegt.</summary>
    private static IEnumerable<string> Silben(string bezeichner) =>
        Zerlegung.Split(bezeichner).Where(silbe => silbe.Length > 0);

    private static readonly Regex Zerlegung =
        new(@"(?<!^)(?=[A-Z0-9])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Der Name einer Assembly, ohne Kultur und Schlüssel.</summary>
    private static string Kurzname(Assembly flaeche) =>
        flaeche.GetName().Name ?? "unbenannt";
}
