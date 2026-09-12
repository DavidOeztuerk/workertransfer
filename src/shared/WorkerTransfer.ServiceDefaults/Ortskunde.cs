using System.Globalization;
using System.Reflection;
using System.Text;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Ein Punkt auf der Erde.</summary>
/// <param name="Breite">Breitengrad in Dezimalgrad.</param>
/// <param name="Laenge">Längengrad in Dezimalgrad.</param>
public readonly record struct Ortspunkt(double Breite, double Laenge);

/// <summary>
/// Übersetzt eine Postleitzahl oder einen Ortsnamen in Koordinaten — aus einer
/// mitgelieferten Tabelle.
/// </summary>
/// <remarks>
/// <strong>Keine Anfrage nach draussen, und das ist der Punkt.</strong> Ein
/// Geokodierdienst (Nominatim, Google, Mapbox) würde bei jedem Speichern einer
/// Anzeige den Ort eines Unternehmens an einen fremden Server geben — auf einer
/// Plattform, deren Datenschutzseite „keine Drittanbieter" verspricht, wäre das
/// eine Ausnahme, die niemand entschieden hat. Und er brächte Rate-Limit,
/// Ausfallverhalten und eine Nachwanderung mit.
///
/// <para>
/// <strong>Die Abdeckung ist deshalb trotzdem vollständig</strong>, weil die
/// Daten klein genug sind, um sie mitzuliefern: <c>Postleitzahlen.txt</c> hält
/// alle Postleitzahlen und alle nennenswerten Orte in DE, AT und CH (GeoNames,
/// CC BY 4.0 — siehe <c>NOTICE.md</c>). Erzeugt von
/// <c>scripts/plz-tabelle.py</c>; wer sie erneuern will, ruft das Skript.
/// </para>
///
/// <para>
/// <strong>Was mehrdeutig ist, bleibt unbekannt.</strong> „Neustadt" gibt es
/// zwanzigmal in ähnlicher Grösse — dafür einen Punkt zu erfinden hiesse, eine
/// Stelle um vierhundert Kilometer zu verschieben, ohne dass es jemand merkt.
/// <c>null</c> heisst „darüber wissen wir nichts", wird gezählt und der
/// suchenden Person genannt (ADR-0032). „Husum" dagegen löst auf: der Ort in
/// Nordfriesland überragt seine vier Namensvettern um das Neunfache.
/// </para>
/// </remarks>
public static class Ortskunde
{
    /// <summary>
    /// Bis zu wie vielen Wörtern eine Ortsangabe noch in Teilstücke zerlegt
    /// wird. Siehe <see cref="Teilstuecke"/> — dort steht der Grund.
    /// </summary>
    private const int HoechsteWortzahlFuerTeilworte = 3;

    private static readonly Lazy<Tabelle> Geladen = new(Lies, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <param name="NachPlz">
    /// Postleitzahl auf Kandidaten. Mehrere, weil AT und CH sich den
    /// vierstelligen Bereich teilen — aufgelöst wird das über den Ortsnamen.
    /// </param>
    /// <param name="NachName">Normalisierter Ortsname auf Punkt.</param>
    private sealed record Tabelle(
        Dictionary<string, List<(Ortspunkt Punkt, string Ort)>> NachPlz,
        Dictionary<string, Ortspunkt> NachName);

    /// <summary>Findet die Koordinaten zu einer Ortsangabe.</summary>
    /// <param name="ort">Was in der Anzeige steht. Freitext.</param>
    /// <returns>Der Punkt, oder <c>null</c>, wenn der Ort unbekannt ist.</returns>
    public static Ortspunkt? Finde(string? ort) => Finde(null, ort);

    /// <summary>Findet die Koordinaten zu Postleitzahl und Ortsangabe.</summary>
    /// <remarks>
    /// <strong>Die Postleitzahl zuerst</strong>, weil sie eindeutig ist und der
    /// Ortsname es nicht sein muss. Der Ortsname wird dabei mitgelesen, nicht
    /// ignoriert: bei einem vierstelligen Code, den Österreich und die Schweiz
    /// sich teilen, entscheidet er — und ohne ihn bleibt so ein Code unbekannt,
    /// statt dass die Schweiz für Österreich einspringt.
    /// </remarks>
    /// <param name="postleitzahl">Was im Feld steht, oder <c>null</c>.</param>
    /// <param name="ort">Was in der Anzeige steht. Freitext.</param>
    /// <returns>Der Punkt, oder <c>null</c>.</returns>
    public static Ortspunkt? Finde(string? postleitzahl, string? ort)
    {
        var tabelle = Geladen.Value;
        var teile = Teilstuecke(ort);

        if (AusPlz(tabelle, postleitzahl, teile) is { } ausFeld)
        {
            return ausFeld;
        }

        // Der längste Treffer gewinnt: „Frankfurt am Main" darf nicht an
        // „Frankfurt" hängenbleiben, und „Raum Stuttgart" muss Stuttgart
        // finden.
        foreach (var stueck in teile)
        {
            if (tabelle.NachName.TryGetValue(stueck, out var punkt))
            {
                return punkt;
            }

            // Eine Postleitzahl MITTEN im Freitext („10115 Berlin"). Alte
            // Anzeigen haben kein eigenes Feld dafür, und ihnen deswegen den
            // Ort abzusprechen wäre eine Lücke ohne Grund.
            if (stueck.Length is 4 or 5
                && stueck.All(char.IsAsciiDigit)
                && AusPlz(tabelle, stueck, teile) is { } ausText)
            {
                return ausText;
            }
        }

        return null;
    }

    private static Ortspunkt? AusPlz(
        Tabelle tabelle, string? postleitzahl, IReadOnlyList<string> teile)
    {
        if (string.IsNullOrWhiteSpace(postleitzahl))
        {
            return null;
        }

        // „D-10115", „A-1010", „CH-8001": das Länderkürzel davor ist üblich und
        // gehört nicht zum Schlüssel.
        var schluessel = new string([.. postleitzahl.Where(char.IsAsciiDigit)]);

        if (schluessel.Length == 0
            || !tabelle.NachPlz.TryGetValue(schluessel, out var kandidaten))
        {
            return null;
        }

        if (kandidaten.Count == 1)
        {
            return kandidaten[0].Punkt;
        }

        // Mehrdeutig — der Ortsname entscheidet. Tut er es nicht, bleibt es
        // unbekannt: einen der Kandidaten zu greifen wäre ein Münzwurf zwischen
        // zwei Ländern.
        foreach (var kandidat in kandidaten)
        {
            if (teile.Contains(Normalisiere(kandidat.Ort)))
            {
                return kandidat.Punkt;
            }
        }

        return null;
    }

    /// <summary>Der Erdradius, in Kilometern.</summary>
    private const double ErdradiusKm = 6371.0;

    /// <summary>Die Entfernung zweier Punkte auf der Erdoberfläche.</summary>
    /// <remarks>
    /// Haversine. Genau genug für eine Umkreissuche: der Fehler gegenüber einem
    /// Ellipsoidmodell liegt unter einem halben Prozent, und wer „50 km" wählt,
    /// meint keine 50,0 km.
    /// </remarks>
    /// <param name="a">Der eine Punkt.</param>
    /// <param name="b">Der andere.</param>
    /// <returns>Die Entfernung in Kilometern.</returns>
    public static double EntfernungKm(Ortspunkt a, Ortspunkt b)
    {
        var dBreite = Bogenmass(b.Breite - a.Breite);
        var dLaenge = Bogenmass(b.Laenge - a.Laenge);

        var h = (Math.Sin(dBreite / 2) * Math.Sin(dBreite / 2))
                + (Math.Cos(Bogenmass(a.Breite)) * Math.Cos(Bogenmass(b.Breite))
                   * Math.Sin(dLaenge / 2) * Math.Sin(dLaenge / 2));

        return ErdradiusKm * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    private static double Bogenmass(double grad) => grad * Math.PI / 180.0;

    /// <summary>Wie weit ein Punkt höchstens von einem bekannten Ort weg sein darf.</summary>
    /// <remarks>
    /// Fünfzig Kilometer. Wer weiter weg steht, ist nicht in DE, AT oder CH —
    /// und dann den nächsten deutschen Ort zurückzugeben wäre eine Behauptung
    /// über den Aufenthaltsort, die niemand aufgestellt hat.
    /// </remarks>
    private const double HoechsteRueckwaertsEntfernungKm = 50.0;

    /// <summary>Der nächstgelegene bekannte Ort zu einem Punkt.</summary>
    /// <remarks>
    /// <strong>Die Gegenrichtung, und sie hat genau einen Zweck:</strong> die
    /// Oberfläche schreibt nach einem Druck auf „Meinen Standort verwenden" den
    /// gefundenen Ort in das Ortsfeld, damit die suchende Person SIEHT, wovon
    /// aus gemessen wird. Ein Umkreis um einen unsichtbaren Punkt ist eine
    /// Zumutung.
    /// <para>
    /// Gesucht wird in der Postleitzahltabelle und nicht im Namensregister: dort
    /// steht je Eintrag ein amtlicher Ortsname, während das Namensregister auch
    /// Schreibvarianten und fremdsprachige Formen führt — „Munich" in ein
    /// deutsches Formular zu schreiben wäre albern.
    /// </para>
    /// </remarks>
    /// <param name="punkt">Wo die Person steht.</param>
    /// <returns>Der Ortsname, oder <c>null</c>, wenn nichts in der Nähe liegt.</returns>
    public static string? NaechsterOrt(Ortspunkt punkt)
    {
        string? bester = null;
        var kuerzeste = HoechsteRueckwaertsEntfernungKm;

        foreach (var kandidaten in Geladen.Value.NachPlz.Values)
        {
            foreach (var (ziel, ort) in kandidaten)
            {
                var entfernung = EntfernungKm(punkt, ziel);

                if (entfernung < kuerzeste)
                {
                    kuerzeste = entfernung;
                    bester = ort;
                }
            }
        }

        // „Wien, Innere Stadt" ist ein Stadtteil. In ein Ortsfeld gehört „Wien".
        var komma = bester?.IndexOf(',');

        return komma is > 0 ? bester![..komma.Value].Trim() : bester;
    }

    /// <summary>Wie viele Postleitzahlen und Orte die Tabelle kennt.</summary>
    /// <remarks>
    /// Für einen Test, der beweist, dass die eingebettete Datei überhaupt
    /// mitgeliefert wurde. Ohne ihn wäre eine leere Tabelle von „kein Ort
    /// bekannt" nicht zu unterscheiden — und das sähe aus wie eine
    /// Umkreissuche, die nichts findet, statt wie eine fehlende Datei.
    /// </remarks>
    public static (int Postleitzahlen, int Orte) Umfang() =>
        (Geladen.Value.NachPlz.Count, Geladen.Value.NachName.Count);

    /// <summary>
    /// Zerlegt eine Ortsangabe in die Teilstücke, die ein Ortsname sein
    /// könnten — das längste zuerst.
    /// </summary>
    /// <remarks>
    /// Drei Stufen, und die dritte ist bewusst eingeschränkt:
    /// <list type="number">
    /// <item>der ganze Text („Frankfurt am Main"),</item>
    /// <item>jedes Stück zwischen Komma, Schrägstrich, Klammer oder Doppelpunkt
    /// („Berlin, Mitte" → „berlin"; „Standort: München" → „muenchen"),</item>
    /// <item>einzelne Wortfolgen — aber NUR, wenn das Stück höchstens
    /// <see cref="HoechsteWortzahlFuerTeilworte"/> Wörter hat.</item>
    /// </list>
    ///
    /// <para>
    /// <strong>Die Grenze in Stufe drei ist gemessen, nicht gewählt.</strong>
    /// Ohne sie fand „Auf dem Hof meiner Oma" die Stadt <em>Hof</em> in Bayern
    /// (46000 Einwohner) und legte die Anzeige dorthin. Dieselbe Falle stellen
    /// „Essen", „Halle", „Lage", „Brand" und „Bad" — alles gewöhnliche Wörter
    /// und alles Ortsnamen. Aus einer Wortfolge <em>ein</em> Wort zu greifen
    /// ist bei zwei Wörtern eine Lesart („Raum Stuttgart") und bei fünf ein
    /// Ratespiel. Wer prosaisch schreibt, wird über die POSTLEITZAHL gefunden;
    /// dafür hat die Anzeige ein Feld.
    /// </para>
    ///
    /// <para>
    /// Der Bindestrich trennt NICHT — sonst zerfiele „Baden-Baden" in zwei
    /// Wörter, von denen eines woanders liegt.
    /// </para>
    ///
    /// <para>
    /// Und absichtlich so statt als Suche „kommt ein bekannter Ort im Text
    /// vor": die Tabelle hat sechsundzwanzigtausend Namen, und ein Durchgang
    /// darüber je Anzeige wären Millionen Vergleiche für eine Antwort.
    /// </para>
    /// </remarks>
    private static List<string> Teilstuecke(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var ganz = Normalisiere(text);
        var stuecke = new List<string> { ganz };

        var abschnitte = ganz.Split([',', '/', '|', '(', ')', ';', ':', '·'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var abschnitt in abschnitte)
        {
            if (abschnitt != ganz)
            {
                stuecke.Add(abschnitt);
            }
        }

        foreach (var abschnitt in abschnitte)
        {
            var woerter = abschnitt.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (woerter.Length is < 2 or > HoechsteWortzahlFuerTeilworte)
            {
                continue;
            }

            for (var laenge = woerter.Length - 1; laenge >= 1; laenge--)
            {
                for (var start = 0; start + laenge <= woerter.Length; start++)
                {
                    stuecke.Add(string.Join(' ', woerter, start, laenge));
                }
            }
        }

        return stuecke;
    }

    /// <summary>Kleinschreibung, Umlaute ausgeschrieben, Akzente weg.</summary>
    private static string Normalisiere(string text)
    {
        var gebaut = new StringBuilder(text.Length);

        foreach (var zeichen in text.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            // Erst die deutschen Sonderfälle: „ä" wird „ae" und nicht „a".
            // Wer sie über die Zerlegung in Grundzeichen laufen liesse, machte
            // aus „München" ein „munchen", das keine Schreibweise ist.
            _ = zeichen switch
            {
                'ä' => gebaut.Append("ae"),
                'ö' => gebaut.Append("oe"),
                'ü' => gebaut.Append("ue"),
                'ß' => gebaut.Append("ss"),
                _ => gebaut.Append(zeichen)
            };
        }

        // Und dann der Rest der Akzente — „Genève" und „Geneve" sind derselbe
        // Ort, und die Tabelle trägt beide Schreibweisen nicht.
        var zerlegt = gebaut.ToString().Normalize(NormalizationForm.FormD);
        var ohne = new StringBuilder(zerlegt.Length);

        foreach (var zeichen in zerlegt)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(zeichen) != UnicodeCategory.NonSpacingMark)
            {
                _ = ohne.Append(zeichen);
            }
        }

        return ohne.ToString().Normalize(NormalizationForm.FormC);
    }

    private static Tabelle Lies()
    {
        var nachPlz = new Dictionary<string, List<(Ortspunkt, string)>>(StringComparer.Ordinal);
        var nachName = new Dictionary<string, Ortspunkt>(StringComparer.Ordinal);

        using var strom = typeof(Ortskunde).Assembly.GetManifestResourceStream(
            "WorkerTransfer.ServiceDefaults.Postleitzahlen.txt")
            ?? throw new InvalidOperationException(
                "Postleitzahlen.txt fehlt im Bild. Ohne sie kennt die "
                + "Umkreissuche keinen einzigen Ort.");

        using var leser = new StreamReader(strom, Encoding.UTF8);

        var inOrten = false;

        while (leser.ReadLine() is { } zeile)
        {
            if (zeile.Length == 0 || zeile[0] == '#')
            {
                continue;
            }

            if (zeile[0] == '[')
            {
                inOrten = zeile == "[orte]";
                continue;
            }

            var felder = zeile.Split('\t');

            if (inOrten)
            {
                if (felder.Length >= 3 && Punkt(felder[1], felder[2]) is { } punkt)
                {
                    // Zwei Schreibweisen desselben Ortes ergeben denselben
                    // Schlüssel — dann steht dort schon der gleiche Punkt.
                    nachName[Normalisiere(felder[0])] = punkt;
                }

                continue;
            }

            if (felder.Length >= 5 && Punkt(felder[2], felder[3]) is { } ziel)
            {
                if (!nachPlz.TryGetValue(felder[1], out var liste))
                {
                    liste = [];
                    nachPlz[felder[1]] = liste;
                }

                liste.Add((ziel, felder[4]));
            }
        }

        return new Tabelle(nachPlz, nachName);
    }

    private static Ortspunkt? Punkt(string breite, string laenge) =>
        double.TryParse(breite, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
        && double.TryParse(laenge, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
            ? new Ortspunkt(b, l)
            : null;
}
