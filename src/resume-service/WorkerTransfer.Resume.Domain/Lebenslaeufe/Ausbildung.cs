namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>Schule oder berufliche Ausbildung — beides Bildung, nicht dieselbe.</summary>
public enum Ausbildungsart
{
    /// <summary>Lehre, Umschulung, berufliche Qualifikation.</summary>
    Ausbildung,

    /// <summary>Allgemeinbildende Schule.</summary>
    Schule
}

/// <summary>One stretch of education.</summary>
/// <remarks>
/// The qualification is optional and stays optional: somebody who broke off a
/// degree still spent those years somewhere, and a required field would force
/// them either to lie or to leave the whole entry out.
/// </remarks>
public sealed record Ausbildung
{
    private Ausbildung(
        string einrichtung,
        string abschluss,
        Monat beginn,
        Monat? ende,
        Ausbildungsart art)
    {
        Einrichtung = einrichtung;
        Abschluss = abschluss;
        Beginn = beginn;
        Ende = ende;
        Art = art;
    }

    /// <summary>Where.</summary>
    public string Einrichtung { get; }

    /// <summary>What came of it, where anything did.</summary>
    public string Abschluss { get; }

    /// <summary>From when.</summary>
    public Monat Beginn { get; }

    /// <summary><c>null</c> means "still studying".</summary>
    public Monat? Ende { get; }

    /// <summary>Schule oder berufliche Ausbildung.</summary>
    public Ausbildungsart Art { get; }

    /// <summary>Whether it is still running.</summary>
    public bool Laeuft => Ende is null;

    /// <summary>Builds one, or refuses.</summary>
    /// <exception cref="Lebenslaufregel">A field or the span is not valid.</exception>
    public static Ausbildung Aus(
        string einrichtung,
        string abschluss,
        Monat beginn,
        Monat? ende = null,
        Ausbildungsart art = Ausbildungsart.Ausbildung)
    {
        ArgumentNullException.ThrowIfNull(beginn);

        Text.PruefeSpanne(beginn, ende);

        return new Ausbildung(
            Text.Gepruegt("Einrichtung", einrichtung, pflicht: true, Text.HoechsteNamenslaenge),
            Text.Gepruegt("Abschluss", abschluss, pflicht: false, Text.HoechsteNamenslaenge),
            beginn,
            ende,
            art);
    }
}
