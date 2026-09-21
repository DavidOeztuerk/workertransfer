using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkerTransfer.Companies.Domain.EmployerProfiles;

/// <summary>The address of the careers page.</summary>
/// <remarks>
/// <strong>Derived and not typed in</strong>: a free field is an invitation to
/// occupy somebody else's name.
/// </remarks>
public static partial class Slug
{
    /// <summary>How long a slug gets at most.</summary>
    public const int MaxLength = 60;

    /// <summary>When nothing usable is left of the name.</summary>
    /// <remarks>
    /// With a purely Chinese name, for instance. The counter at save time
    /// makes <c>unternehmen-2</c> and so on out of it; an empty address would
    /// be worse than an impersonal one.
    /// </remarks>
    public const string Fallback = "unternehmen";

    /// <summary>Derives a slug from the display name.</summary>
    /// <remarks>
    /// Umlauts are decomposed and their base letters kept — "Grün" becomes
    /// <c>gruen</c> only with a substitution table, <c>grun</c> without one;
    /// the latter is more honest than a table that is wrong at the next
    /// language.
    /// </remarks>
    public static string From(string? displayName)
    {
        var decomposed = (displayName ?? string.Empty)
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormKD);

        var asciiOnly = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            // Combining marks fall away, and so does everything else beyond
            // ASCII — the separator below turns it into a gap anyway.
            if (character < 128
                && CharUnicodeInfo.GetUnicodeCategory(character)
                    != UnicodeCategory.NonSpacingMark)
            {
                asciiOnly.Append(character);
            }
        }

        var separated = NotAllowed()
            .Replace(asciiOnly.ToString(), "-")
            .Trim('-');

        if (separated.Length > MaxLength)
        {
            separated = separated[..MaxLength].Trim('-');
        }

        return separated.Length == 0 ? Fallback : separated;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotAllowed();
}
