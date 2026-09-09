namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>The two text rules every field of a résumé obeys.</summary>
internal static class Text
{
    internal const int HoechsteNamenslaenge = 160;

    internal const int HoechsteBeschreibungslaenge = 2000;

    internal static string Gepruegt(string feld, string? wert, bool pflicht, int grenze)
    {
        var sauber = (wert ?? string.Empty).Trim();

        if (pflicht && sauber.Length == 0)
        {
            throw new Lebenslaufregel(Regelcodes.Text, $"{feld} darf nicht leer sein.");
        }

        return sauber.Length > grenze
            ? throw new Lebenslaufregel(
                Regelcodes.Text, $"{feld} überschreitet {grenze} Zeichen.")
            : sauber;
    }

    /// <summary>An end before its start is not a span.</summary>
    /// <remarks>
    /// The same month is allowed: a one-month probation is short, not a typo.
    /// </remarks>
    internal static void PruefeSpanne(Monat beginn, Monat? ende)
    {
        if (ende is not null && ende < beginn)
        {
            throw new Lebenslaufregel(
                Regelcodes.Monat, $"Das Ende {ende} liegt vor dem Beginn {beginn}.");
        }
    }
}
