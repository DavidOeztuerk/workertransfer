namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>The two text rules every field of a résumé obeys.</summary>
internal static class Text
{
    internal const int HoechsteNamenslaenge = 160;

    internal const int HoechsteBeschreibungslaenge = 2000;

    internal static string Gepruegt(string field, string? value, bool pflicht, int limit)
    {
        var sauber = (value ?? string.Empty).Trim();

        if (pflicht && sauber.Length == 0)
        {
            throw new ResumeRule(Regelcodes.Text, $"{field} darf nicht leer sein.");
        }

        return sauber.Length > limit
            ? throw new ResumeRule(
                Regelcodes.Text, $"{field} überschreitet {limit} Zeichen.")
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
            throw new ResumeRule(
                Regelcodes.Monat, $"Das Ende {ende} liegt vor dem Beginn {beginn}.");
        }
    }
}
