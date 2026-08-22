namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>One position: a real employer, with a span.</summary>
/// <remarks>
/// This is what separates a résumé from a profile. A profile is a notice board
/// somebody wrote about themselves; this names the company they work for right
/// now, and it is exactly what a current employer must not be shown.
/// </remarks>
public sealed record Station
{
    private Station(string arbeitgeber, string titel, Monat beginn, Monat? ende, string beschreibung)
    {
        Arbeitgeber = arbeitgeber;
        Titel = titel;
        Beginn = beginn;
        Ende = ende;
        Beschreibung = beschreibung;
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

    /// <summary>Whether this is the position they hold now.</summary>
    public bool Laeuft => Ende is null;

    /// <summary>Builds one, or refuses.</summary>
    /// <exception cref="Lebenslaufregel">A field or the span is not valid.</exception>
    public static Station Aus(
        string arbeitgeber,
        string titel,
        Monat beginn,
        Monat? ende = null,
        string beschreibung = "")
    {
        ArgumentNullException.ThrowIfNull(beginn);

        Text.PruefeSpanne(beginn, ende);

        return new Station(
            Text.Gepruegt("Arbeitgeber", arbeitgeber, pflicht: true, Text.HoechsteNamenslaenge),
            Text.Gepruegt("Titel", titel, pflicht: true, Text.HoechsteNamenslaenge),
            beginn,
            ende,
            Text.Gepruegt(
                "Beschreibung", beschreibung, pflicht: false, Text.HoechsteBeschreibungslaenge));
    }
}
