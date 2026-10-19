using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkerTransfer.Resume.Domain.Resumes;

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
public sealed partial record Month : IComparable<Month>
{
    private Month(int year, int number)
    {
        Year = year;
        Number = number;
    }

    /// <summary>The year, four digits.</summary>
    public int Year { get; }

    /// <summary>The month, 1 to 12.</summary>
    public int Number { get; }

    /// <summary>Reads <c>YYYY-MM</c>, or throws.</summary>
    /// <exception cref="ResumeRule">The text is not a month.</exception>
    public static Month Parse(string value) =>
        TryParse(value, out var month)
            ? month
            : throw new ResumeRule(
                RuleCodes.Month, $"expected YYYY-MM, read '{value}'");

    /// <summary>Reads <c>YYYY-MM</c> without throwing.</summary>
    public static bool TryParse(string? value, out Month month)
    {
        month = null!;

        if (value is null)
        {
            return false;
        }

        var hit = Pattern().Match(value.Trim());

        if (!hit.Success)
        {
            return false;
        }

        month = new Month(
            int.Parse(hit.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(hit.Groups[2].Value, CultureInfo.InvariantCulture));

        return true;
    }

    /// <inheritdoc />
    public int CompareTo(Month? other) =>
        other is null ? 1 : (Year, Number).CompareTo((other.Year, other.Number));

    /// <summary>Chronologically earlier.</summary>
    public static bool operator <(Month? left, Month? right) => Compare(left, right) < 0;

    /// <summary>Chronologically later.</summary>
    public static bool operator >(Month? left, Month? right) => Compare(left, right) > 0;

    /// <summary>Earlier or the same month.</summary>
    public static bool operator <=(Month? left, Month? right) => Compare(left, right) <= 0;

    /// <summary>Later or the same month.</summary>
    public static bool operator >=(Month? left, Month? right) => Compare(left, right) >= 0;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Number:D2}");

    private static int Compare(Month? left, Month? right) => left switch
    {
        null when right is null => 0,
        null => -1,
        _ => left.CompareTo(right)
    };

    [GeneratedRegex(@"^(\d{4})-(0[1-9]|1[0-2])$")]
    private static partial Regex Pattern();
}
