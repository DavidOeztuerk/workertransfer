using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>A month, never a day.</summary>
/// <remarks>
/// No résumé in the world names the 14th of March. A day suggests a precision
/// nobody has, and it turns a three-week gap into something that has to be
/// justified.
/// <para>
/// A class and not a <c>struct</c> on purpose: a struct has a
/// <c>default</c> — year zero, month zero — that sorts before every real month
/// and would slip into an ordering as the oldest position anybody ever held.
/// A reference type has no such value, and <c>null</c> already means something
/// here ("still running").
/// </para>
/// </remarks>
public sealed partial record Monat : IComparable<Monat>
{
    private Monat(int jahr, int nummer)
    {
        Jahr = jahr;
        Nummer = nummer;
    }

    /// <summary>The year, four digits.</summary>
    public int Jahr { get; }

    /// <summary>The month, 1 to 12.</summary>
    public int Nummer { get; }

    /// <summary>Reads <c>YYYY-MM</c>, or throws.</summary>
    /// <exception cref="Lebenslaufregel">The text is not a month.</exception>
    public static Monat Lies(string wert) =>
        Versuche(wert, out var monat)
            ? monat
            : throw new Lebenslaufregel(
                Regelcodes.Monat, $"Erwartet wurde YYYY-MM, gelesen wurde '{wert}'.");

    /// <summary>Reads <c>YYYY-MM</c> without throwing.</summary>
    public static bool Versuche(string? wert, out Monat monat)
    {
        monat = null!;

        if (wert is null)
        {
            return false;
        }

        var treffer = Muster().Match(wert.Trim());

        if (!treffer.Success)
        {
            return false;
        }

        monat = new Monat(
            int.Parse(treffer.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(treffer.Groups[2].Value, CultureInfo.InvariantCulture));

        return true;
    }

    /// <inheritdoc />
    public int CompareTo(Monat? other) =>
        other is null ? 1 : (Jahr, Nummer).CompareTo((other.Jahr, other.Nummer));

    /// <summary>Chronologically earlier.</summary>
    public static bool operator <(Monat? links, Monat? rechts) => Vergleiche(links, rechts) < 0;

    /// <summary>Chronologically later.</summary>
    public static bool operator >(Monat? links, Monat? rechts) => Vergleiche(links, rechts) > 0;

    /// <summary>Earlier or the same month.</summary>
    public static bool operator <=(Monat? links, Monat? rechts) => Vergleiche(links, rechts) <= 0;

    /// <summary>Later or the same month.</summary>
    public static bool operator >=(Monat? links, Monat? rechts) => Vergleiche(links, rechts) >= 0;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Jahr:D4}-{Nummer:D2}");

    private static int Vergleiche(Monat? links, Monat? rechts) => links switch
    {
        null when rechts is null => 0,
        null => -1,
        _ => links.CompareTo(rechts)
    };

    [GeneratedRegex(@"^(\d{4})-(0[1-9]|1[0-2])$")]
    private static partial Regex Muster();
}
