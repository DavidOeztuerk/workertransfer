using System.Globalization;
using System.Reflection;
using System.Text;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>A point on the earth.</summary>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
public readonly record struct PlacePoint(double Latitude, double Longitude);

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
/// <c>NOTICE.md</c>). Produced by <c>scripts/plz-table.py</c>; whoever wants
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
    /// <see cref="Segments"/> — the reason stands there.
    /// </summary>
    private const int MaxWordsPerSegment = 3;

    private static readonly Lazy<Table> Loaded = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <param name="ByPostalCode">
    /// Postcode to candidates. Several, because AT and CH share the four-digit
    /// range — that is resolved through the place name.
    /// </param>
    /// <param name="ByName">Normalised place name to point.</param>
    private sealed record Table(
        Dictionary<string, List<(PlacePoint Point, string Place)>> ByPostalCode,
        Dictionary<string, PlacePoint> ByName);

    /// <summary>Finds the coordinates for a location.</summary>
    /// <param name="place">What stands in the advert. Free text.</param>
    /// <returns>The point, or <c>null</c> when the place is unknown.</returns>
    public static PlacePoint? Find(string? place) => Find(null, place);

    /// <summary>Finds the coordinates for a postcode and a location.</summary>
    /// <remarks>
    /// <strong>The postcode first</strong>, because it is unambiguous and the
    /// place name need not be. The place name is read along with it, not
    /// ignored: with a four-digit code that Austria and Switzerland share, it
    /// decides — and without it such a code stays unknown instead of
    /// Switzerland standing in for Austria.
    /// </remarks>
    /// <param name="postalCode">What stands in the field, or <c>null</c>.</param>
    /// <param name="place">What stands in the advert. Free text.</param>
    /// <returns>The point, or <c>null</c>.</returns>
    public static PlacePoint? Find(string? postalCode, string? place)
    {
        var table = Loaded.Value;
        var segments = Segments(place);

        if (FromPostalCode(table, postalCode, segments) is { } fromField)
        {
            return fromField;
        }

        // The longest hit wins: "Frankfurt am Main" must not get stuck on
        // "Frankfurt", and "Raum Stuttgart" has to find Stuttgart.
        foreach (var segment in segments)
        {
            if (table.ByName.TryGetValue(segment, out var point))
            {
                return point;
            }

            // A postcode IN THE MIDDLE of the free text ("10115 Berlin"). Old
            // adverts have no field of their own for it, and denying them their
            // location for that reason would be a gap without a reason.
            if (segment.Length is 4 or 5
                && segment.All(char.IsAsciiDigit)
                && FromPostalCode(table, segment, segments) is { } fromText)
            {
                return fromText;
            }
        }

        return null;
    }

    private static PlacePoint? FromPostalCode(
        Table table, string? postalCode, IReadOnlyList<string> segments)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return null;
        }

        // "D-10115", "A-1010", "CH-8001": the country prefix is common and is
        // not part of the key.
        var key = new string([.. postalCode.Where(char.IsAsciiDigit)]);

        if (key.Length == 0
            || !table.ByPostalCode.TryGetValue(key, out var candidates))
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0].Point;
        }

        // Ambiguous — the place name decides. If it does not, it stays
        // unknown: grabbing one of the candidates would be a coin toss between
        // two countries.
        foreach (var candidate in candidates)
        {
            if (segments.Contains(Normalise(candidate.Place)))
            {
                return candidate.Point;
            }
        }

        return null;
    }

    /// <summary>The earth's radius, in kilometres.</summary>
    private const double EarthRadiusKm = 6371.0;

    /// <summary>The distance between two points on the earth's surface.</summary>
    /// <remarks>
    /// Haversine. Accurate enough for a radius search: the error against an
    /// ellipsoid model is below half a percent, and whoever chooses "50 km"
    /// does not mean 50.0 km.
    /// </remarks>
    /// <param name="a">The one point.</param>
    /// <param name="b">The other.</param>
    /// <returns>The distance in kilometres.</returns>
    public static double DistanceKm(PlacePoint a, PlacePoint b)
    {
        var dLatitude = Radians(b.Latitude - a.Latitude);
        var dLongitude = Radians(b.Longitude - a.Longitude);

        var h = (Math.Sin(dLatitude / 2) * Math.Sin(dLatitude / 2))
                + (Math.Cos(Radians(a.Latitude)) * Math.Cos(Radians(b.Latitude))
                   * Math.Sin(dLongitude / 2) * Math.Sin(dLongitude / 2));

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>How far a point may at most be from a known place.</summary>
    /// <remarks>
    /// Fifty kilometres. Whoever stands further away is not in DE, AT or CH —
    /// and returning the nearest German place then would be a claim about
    /// somebody's whereabouts that nobody made.
    /// </remarks>
    private const double MaxReverseDistanceKm = 50.0;

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
    /// <param name="point">Where the person stands.</param>
    /// <returns>The place name, or <c>null</c> when nothing lies nearby.</returns>
    public static string? NearestPlace(PlacePoint point)
    {
        string? best = null;
        var shortest = MaxReverseDistanceKm;

        foreach (var candidates in Loaded.Value.ByPostalCode.Values)
        {
            foreach (var (target, place) in candidates)
            {
                var distance = DistanceKm(point, target);

                if (distance < shortest)
                {
                    shortest = distance;
                    best = place;
                }
            }
        }

        // "Wien, Innere Stadt" is a district. A location field takes "Wien".
        var comma = best?.IndexOf(',');

        return comma is > 0 ? best![..comma.Value].Trim() : best;
    }

    /// <summary>How many postcodes and places the table knows.</summary>
    /// <remarks>
    /// For a test that proves the embedded file was shipped at all. Without it
    /// an empty table would be indistinguishable from "no place known" — and
    /// that would look like a radius search finding nothing instead of a
    /// missing file.
    /// </remarks>
    public static (int PostalCodes, int Places) Size() =>
        (Loaded.Value.ByPostalCode.Count, Loaded.Value.ByName.Count);

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
    /// <see cref="MaxWordsPerSegment"/> words.</item>
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
    private static List<string> Segments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var whole = Normalise(text);
        var segmentList = new List<string> { whole };

        var sections = whole.Split([',', '/', '|', '(', ')', ';', ':', '·'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var section in sections)
        {
            if (section != whole)
            {
                segmentList.Add(section);
            }
        }

        foreach (var section in sections)
        {
            var words = section.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length is < 2 or > MaxWordsPerSegment)
            {
                continue;
            }

            for (var length = words.Length - 1; length >= 1; length--)
            {
                for (var start = 0; start + length <= words.Length; start++)
                {
                    segmentList.Add(string.Join(' ', words, start, length));
                }
            }
        }

        return segmentList;
    }

    /// <summary>Lower case, umlauts written out, accents gone.</summary>
    private static string Normalise(string text)
    {
        var built = new StringBuilder(text.Length);

        foreach (var character in text.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            // The German special cases first: "ä" becomes "ae" and not "a".
            // Letting them run through the decomposition into base characters
            // would turn "München" into a "munchen", which is no spelling at
            // all.
            _ = character switch
            {
                'ä' => built.Append("ae"),
                'ö' => built.Append("oe"),
                'ü' => built.Append("ue"),
                'ß' => built.Append("ss"),
                _ => built.Append(character)
            };
        }

        // And then the rest of the accents — "Genève" and "Geneve" are the
        // same place, and the table does not carry both spellings.
        var decomposed = built.ToString().Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                _ = stripped.Append(character);
            }
        }

        return stripped.ToString().Normalize(NormalizationForm.FormC);
    }

    private static Table Read()
    {
        var byPostalCode = new Dictionary<string, List<(PlacePoint, string)>>(StringComparer.Ordinal);
        var byName = new Dictionary<string, PlacePoint>(StringComparer.Ordinal);

        using var stream = typeof(PlaceLookup).Assembly.GetManifestResourceStream(
            "WorkerTransfer.ServiceDefaults.Postleitzahlen.txt")
            ?? throw new InvalidOperationException(
                "Postleitzahlen.txt fehlt im Bild. Ohne sie kennt die "
                + "Umkreissuche keinen einzigen Ort.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        var inPlaces = false;

        while (reader.ReadLine() is { } row)
        {
            if (row.Length == 0 || row[0] == '#')
            {
                continue;
            }

            if (row[0] == '[')
            {
                inPlaces = row == "[orte]";
                continue;
            }

            var fields = row.Split('\t');

            if (inPlaces)
            {
                if (fields.Length >= 3 && Point(fields[1], fields[2]) is { } point)
                {
                    // Two spellings of the same place yield the same key —
                    // and then the same point already stands there.
                    byName[Normalise(fields[0])] = point;
                }

                continue;
            }

            if (fields.Length >= 5 && Point(fields[2], fields[3]) is { } target)
            {
                if (!byPostalCode.TryGetValue(fields[1], out var list))
                {
                    list = [];
                    byPostalCode[fields[1]] = list;
                }

                list.Add((target, fields[4]));
            }
        }

        return new Table(byPostalCode, byName);
    }

    private static PlacePoint? Point(string latitude, string length) =>
        double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
        && double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
            ? new PlacePoint(b, l)
            : null;
}
