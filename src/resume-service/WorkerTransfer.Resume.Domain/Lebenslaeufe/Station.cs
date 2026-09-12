using WorkerTransfer.Skills;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>One position: a real employer, with a span.</summary>
/// <remarks>
/// This is what separates a résumé from a profile. A profile is a notice board
/// somebody wrote about themselves; this names the company they work for right
/// now, and it is exactly what a current employer must not be shown.
/// </remarks>
public sealed record Station
{
    private Station(
        string arbeitgeber,
        string titel,
        Monat beginn,
        Monat? ende,
        string beschreibung,
        IReadOnlyList<string> technologien)
    {
        Arbeitgeber = arbeitgeber;
        Titel = titel;
        Beginn = beginn;
        Ende = ende;
        Beschreibung = beschreibung;
        Technologien = technologien;
    }

    /// <summary>Who they worked for.</summary>
    public string Arbeitgeber { get; }

    /// <summary>What they did there.</summary>
    public string Titel { get; }

    /// <summary>From when.</summary>
    public Monat Beginn { get; }

    /// <summary><c>null</c> means "still there" — never "unknown".</summary>
    public Monat? Ende { get; }

    /// <summary>Free text the person wrote about their own work.</summary>
    public string Beschreibung { get; }

    /// <summary>Womit dort gearbeitet wurde — von der Person selbst genannt.</summary>
    /// <remarks>
    /// <strong>Eine Nennung, keine Ableitung.</strong> Niemand rechnet aus einer
    /// Stellenbezeichnung, welche Werkzeuge dazugehören; hier steht, was die
    /// Person geschrieben hat, durch denselben Wortschatz vereinheitlicht wie
    /// im Profil (ADR-0023: benennt um, folgert nie).
    /// <para>
    /// Sie macht diese Station NICHT durchsuchbar. Ein Lebenslauf ist einzeln
    /// freigegeben (ADR-0020), und was hier steht, gehört dem Unternehmen, das
    /// ihn lesen darf. Suchbar wird eine Fähigkeit erst, wenn sie im PROFIL
    /// steht — dorthin kommt sie mit einem Klick, nicht von allein.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Technologien { get; }

    /// <summary>Whether this is the position they hold now.</summary>
    public bool Laeuft => Ende is null;

    /// <summary>Builds one, or refuses.</summary>
    /// <exception cref="Lebenslaufregel">A field or the span is not valid.</exception>
    /// <summary>Wie viele Technologien eine Station nennen darf.</summary>
    /// <remarks>
    /// Zwölf. Wer dreissig aufzählt, sagt über eine einzelne Stelle nichts mehr
    /// — und die Grenze schützt zugleich die Vorschlagsliste im Profil davor,
    /// zu einer Wand aus Wörtern zu werden.
    /// </remarks>
    public const int HoechsteTechnologien = 12;

    /// <summary>Builds one, or refuses.</summary>
    /// <exception cref="Lebenslaufregel">A field or the span is not valid.</exception>
    public static Station Aus(
        string arbeitgeber,
        string titel,
        Monat beginn,
        Monat? ende = null,
        string beschreibung = "",
        IEnumerable<string>? technologien = null)
    {
        ArgumentNullException.ThrowIfNull(beginn);

        Text.PruefeSpanne(beginn, ende);

        return new Station(
            Text.Gepruegt("Arbeitgeber", arbeitgeber, pflicht: true, Text.HoechsteNamenslaenge),
            Text.Gepruegt("Titel", titel, pflicht: true, Text.HoechsteNamenslaenge),
            beginn,
            ende,
            Text.Gepruegt(
                "Beschreibung", beschreibung, pflicht: false, Text.HoechsteBeschreibungslaenge),
            Vereinheitlicht(technologien ?? []));
    }

    /// <summary>Erst vereinheitlichen, dann entdoppeln — die Reihenfolge trägt.</summary>
    /// <remarks>
    /// Andersherum stünden „postgres" und „PostgreSQL" als zwei Einträge da und
    /// würden erst danach beide zu „PostgreSQL" (ADR-0023).
    /// </remarks>
    private static IReadOnlyList<string> Vereinheitlicht(IEnumerable<string> roh)
    {
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var werte = new List<string>();

        foreach (var einzeln in roh)
        {
            var geprueft = Text.Gepruegt(
                "Technologie", einzeln, pflicht: false, Faehigkeitsgrenzen.Hoechstlaenge);

            if (geprueft.Length == 0)
            {
                continue;
            }

            var kanonisch = Wortschatz.Kanonisch(geprueft);

            if (gesehen.Add(kanonisch))
            {
                werte.Add(kanonisch);
            }
        }

        return werte.Count > HoechsteTechnologien
            ? throw new Lebenslaufregel(
                Regelcodes.Menge,
                $"a position names at most {HoechsteTechnologien} technologies")
            : werte;
    }
}
