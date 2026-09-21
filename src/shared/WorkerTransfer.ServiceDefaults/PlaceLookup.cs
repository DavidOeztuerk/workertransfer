using System.Globalization;
using System.Reflection;
using System.Text;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>A point on the earth.</summary>
/// <param name="Breite">Latitude in decimal degrees.</param>
/// <param name="Laenge">Longitude in decimal degrees.</param>
public readonly record struct Ortspunkt(double Breite, double Laenge);

/// <summary>
/// Translates a postcode or a place name into coordinates — out of a table
/// shipped along with it.
/// </summary>
/// <remarks>
/// <strong>No request goes outside, and that is the point.</strong> A geocoding
/// service (Nominatim, Google, Mapbox) would hand a company's location to a
/// foreign server every time an advert is saved — on a platform whose privacy
/// page promises "no third parties" that would be an exception nobody decided.
/// And it would bring a rate limit, failure behaviour and a follow-up migration
/// with it.
///
/// <para>
/// <strong>The coverage is complete all the same</strong>, because the data are
/// small enough to ship: <c>Postleitzahlen.txt</c> holds every postcode and
/// every place of any note in DE, AT and CH (GeoNames, CC BY 4.0 — see
/// <c>NOTICE.md</c>). Produced by <c>scripts/plz-tabelle.py</c>; whoever wants
/// to refresh it calls the script.
/// </para>
///
/// <para>
/// <strong>What is ambiguous stays unknown.</strong> There are twenty
/// "Neustadt" of similar size — inventing a point for that would mean shifting
/// a job by four hundred kilometres without anybody noticing. <c>null</c> means
/// "we know nothing about it", is counted and is named to the searching person
/// (ADR-0032). "Husum", on the other hand, resolves: the place in North Frisia
/// towers nine times over its four namesakes.
/// </para>
/// </remarks>
public static class PlaceLookup
{
    /// <summary>
    /// Up to how many words a location is still split into pieces. See
    /// <see cref="Teilstuecke"/> — the reason stands there.
    /// </summary>
    private const int HoechsteWortzahlFuerTeilworte = 3;

    private static readonly Lazy<Tabelle> Geladen = new(Lies, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <param name="NachPlz">
    /// Postcode to candidates. Several, because AT and CH share the four-digit
    /// range — that is resolved through the place name.
    /// </param>
    /// <param name="NachName">Normalised place name to point.</param>
    private sealed record Tabelle(
        Dictionary<string, List<(Ortspunkt Punkt, string Ort)>> NachPlz,
        Dictionary<string, Ortspunkt> NachName);

    /// <summary>Finds the coordinates for a location.</summary>
    /// <param name="ort">What stands in the advert. Free text.</param>
    /// <returns>The point, or <c>null</c> when the place is unknown.</returns>
    public static Ortspunkt? Find(string? ort) => Find(null, ort);

    /// <summary>Finds the coordinates for a postcode and a location.</summary>
    /// <remarks>
    /// <strong>The postcode first</strong>, because it is unambiguous and the
    /// place name need not be. The place name is read along with it, not
    /// ignored: with a four-digit code that Austria and Switzerland share, it
    /// decides — and without it such a code stays unknown instead of
    /// Switzerland standing in for Austria.
    /// </remarks>
    /// <param name="postleitzahl">What stands in the field, or <c>null</c>.</param>
    /// <param name="ort">What stands in the advert. Free text.</param>
    /// <returns>The point, or <c>null</c>.</returns>
    public static Ortspunkt? Find(string? postleitzahl, string? ort)
    {
        var tabelle = Geladen.Value;
        var teile = Teilstuecke(ort);

        if (AusPlz(tabelle, postleitzahl, teile) is { } ausFeld)
        {
            return ausFeld;
        }

        // The longest hit wins: "Frankfurt am Main" must not get stuck on
        // "Frankfurt", and "Raum Stuttgart" has to find Stuttgart.
        foreach (var stueck in teile)
        {
            if (tabelle.NachName.TryGetValue(stueck, out var punkt))
            {
                return punkt;
            }

            // A postcode IN THE MIDDLE of the free text ("10115 Berlin"). Old
            // adverts have no field of their own for it, and denying them their
            // location for that reason would be a gap without a reason.
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

        // "D-10115", "A-1010", "CH-8001": the country prefix is common and is
        // not part of the key.
        var key = new string([.. postleitzahl.Where(char.IsAsciiDigit)]);

        if (key.Length == 0
            || !tabelle.NachPlz.TryGetValue(key, out var kandidaten))
        {
            return null;
        }

        if (kandidaten.Count == 1)
        {
            return kandidaten[0].Punkt;
        }

        // Ambiguous — the place name decides. If it does not, it stays
        // unknown: grabbing one of the candidates would be a coin toss between
        // two countries.
        foreach (var kandidat in kandidaten)
        {
            if (teile.Contains(Normalise(kandidat.Ort)))
            {
                return kandidat.Punkt;
            }
        }

        return null;
    }

    /// <summary>The earth's radius, in kilometres.</summary>
    private const double ErdradiusKm = 6371.0;

    /// <summary>The distance between two points on the earth's surface.</summary>
    /// <remarks>
    /// Haversine. Accurate enough for a radius search: the error against an
    /// ellipsoid model is below half a percent, and whoever chooses "50 km"
    /// does not mean 50.0 km.
    /// </remarks>
    /// <param name="a">The one point.</param>
    /// <param name="b">The other.</param>
    /// <returns>The distance in kilometres.</returns>
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

    /// <summary>How far a point may at most be from a known place.</summary>
    /// <remarks>
    /// Fifty kilometres. Whoever stands further away is not in DE, AT or CH —
    /// and returning the nearest German place then would be a claim about
    /// somebody's whereabouts that nobody made.
    /// </remarks>
    private const double HoechsteRueckwaertsEntfernungKm = 50.0;

    /// <summary>The nearest known place to a point.</summary>
    /// <remarks>
    /// <strong>The reverse direction, and it has exactly one purpose:</strong>
    /// after a press on "use my location" the interface writes the place it
    /// found into the location field, so that the searching person SEES what
    /// the measurement starts from. A radius around an invisible point is an
    /// imposition.
    /// <para>
    /// The search runs over the postcode table and not over the name register:
    /// there every entry carries an official place name, while the name
    /// register also holds spelling variants and foreign-language forms —
    /// writing "Munich" into a German form would be silly.
    /// </para>
    /// </remarks>
    /// <param name="punkt">Where the person stands.</param>
    /// <returns>The place name, or <c>null</c> when nothing lies nearby.</returns>
    public static string? NaechsterOrt(Ortspunkt punkt)
    {
        string? bester = null;
        var kuerzeste = HoechsteRueckwaertsEntfernungKm;

        foreach (var kandidaten in Geladen.Value.NachPlz.Values)
        {
            foreach (var (target, ort) in kandidaten)
            {
                var entfernung = EntfernungKm(punkt, target);

                if (entfernung < kuerzeste)
                {
                    kuerzeste = entfernung;
                    bester = ort;
                }
            }
        }

        // "Wien, Innere Stadt" is a district. A location field takes "Wien".
        var komma = bester?.IndexOf(',');

        return komma is > 0 ? bester![..komma.Value].Trim() : bester;
    }

    /// <summary>How many postcodes and places the table knows.</summary>
    /// <remarks>
    /// For a test that proves the embedded file was shipped at all. Without it
    /// an empty table would be indistinguishable from "no place known" — and
    /// that would look like a radius search finding nothing instead of a
    /// missing file.
    /// </remarks>
    public static (int Postleitzahlen, int Orte) Umfang() =>
        (Geladen.Value.NachPlz.Count, Geladen.Value.NachName.Count);

    /// <summary>
    /// Splits a location into the pieces that could be a place name — the
    /// longest one first.
    /// </summary>
    /// <remarks>
    /// Three stages, and the third is deliberately restricted:
    /// <list type="number">
    /// <item>the whole text ("Frankfurt am Main"),</item>
    /// <item>every piece between comma, slash, bracket or colon
    /// ("Berlin, Mitte" → "berlin"; "Standort: München" → "muenchen"),</item>
    /// <item>single word sequences — but ONLY when the piece has at most
    /// <see cref="HoechsteWortzahlFuerTeilworte"/> words.</item>
    /// </list>
    ///
    /// <para>
    /// <strong>The limit in stage three is measured, not chosen.</strong>
    /// Without it "Auf dem Hof meiner Oma" found the town <em>Hof</em> in
    /// Bavaria (46,000 inhabitants) and put the advert there. The same trap is
    /// set by "Essen", "Halle", "Lage", "Brand" and "Bad" — all ordinary words
    /// and all place names. Grabbing <em>one</em> word out of a word sequence
    /// is a reading with two words ("Raum Stuttgart") and a guessing game with
    /// five. Whoever writes in prose is found through the POSTCODE; the advert
    /// has a field for that.
    /// </para>
    ///
    /// <para>
    /// The hyphen does NOT separate — otherwise "Baden-Baden" would fall into
    /// two words, one of which lies somewhere else.
    /// </para>
    ///
    /// <para>
    /// And deliberately this way rather than as a search for "a known place
    /// occurs in the text": the table has twenty-six thousand names, and one
    /// pass over it per advert would be millions of comparisons for one answer.
    /// </para>
    /// </remarks>
    private static List<string> Teilstuecke(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var ganz = Normalise(text);
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
            var words = abschnitt.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length is < 2 or > HoechsteWortzahlFuerTeilworte)
            {
                continue;
            }

            for (var length = words.Length - 1; length >= 1; length--)
            {
                for (var start = 0; start + length <= words.Length; start++)
                {
                    stuecke.Add(string.Join(' ', words, start, length));
                }
            }
        }

        return stuecke;
    }

    /// <summary>Lower case, umlauts written out, accents gone.</summary>
    private static string Normalise(string text)
    {
        var gebaut = new StringBuilder(text.Length);

        foreach (var zeichen in text.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            // The German special cases first: "ä" becomes "ae" and not "a".
            // Letting them run through the decomposition into base characters
            // would turn "München" into a "munchen", which is no spelling at
            // all.
            _ = zeichen switch
            {
                'ä' => gebaut.Append("ae"),
                'ö' => gebaut.Append("oe"),
                'ü' => gebaut.Append("ue"),
                'ß' => gebaut.Append("ss"),
                _ => gebaut.Append(zeichen)
            };
        }

        // And then the rest of the accents — "Genève" and "Geneve" are the
        // same place, and the table does not carry both spellings.
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

        using var strom = typeof(PlaceLookup).Assembly.GetManifestResourceStream(
            "WorkerTransfer.ServiceDefaults.Postleitzahlen.txt")
            ?? throw new InvalidOperationException(
                "Postleitzahlen.txt fehlt im Bild. Ohne sie kennt die "
                + "Umkreissuche keinen einzigen Ort.");

        using var leser = new StreamReader(strom, Encoding.UTF8);

        var inOrten = false;

        while (leser.ReadLine() is { } row)
        {
            if (row.Length == 0 || row[0] == '#')
            {
                continue;
            }

            if (row[0] == '[')
            {
                inOrten = row == "[orte]";
                continue;
            }

            var fields = row.Split('\t');

            if (inOrten)
            {
                if (fields.Length >= 3 && Punkt(fields[1], fields[2]) is { } punkt)
                {
                    // Two spellings of the same place yield the same key —
                    // and then the same point already stands there.
                    nachName[Normalise(fields[0])] = punkt;
                }

                continue;
            }

            if (fields.Length >= 5 && Punkt(fields[2], fields[3]) is { } target)
            {
                if (!nachPlz.TryGetValue(fields[1], out var list))
                {
                    list = [];
                    nachPlz[fields[1]] = list;
                }

                list.Add((target, fields[4]));
            }
        }

        return new Tabelle(nachPlz, nachName);
    }

    private static Ortspunkt? Punkt(string breite, string length) =>
        double.TryParse(breite, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
        && double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
            ? new Ortspunkt(b, l)
            : null;
}
