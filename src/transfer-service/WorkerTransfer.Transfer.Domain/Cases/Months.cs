using System.Globalization;

namespace WorkerTransfer.Transfer.Domain.Cases;

/// <summary>A month as the wire carries it: <c>yyyy-MM</c>.</summary>
/// <remarks>
/// One reading for the offer's start and the release month. Free text went
/// into a seven-character column until 07.10.2026, and "Oktober 2026" ended in
/// a 500 — and the notice mark (H2) can only compare what it can read.
/// </remarks>
public static class Months
{
    /// <summary>Whether the text is exactly a month.</summary>
    public static bool IsMonth(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
