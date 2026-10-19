using System.Collections.Frozen;
using System.Text;

namespace WorkerTransfer.Skills;

/// <summary>Which known words occur in a text — and not one more.</summary>
/// <remarks>
/// <strong>It searches for what the <see cref="Vocabulary"/> knows, and never
/// guesses.</strong> That is the whole boundary, and it is the same one as a
/// line further up: <c>Vocabulary</c> renames and does not infer, and this
/// function applies exactly that to a text that was found. It reads the
/// spelling <c>"schweissfachmann"</c> in a reference and offers the canonical
/// name <c>"Schweißfachmann"</c> for it — a statement about <em>language</em>.
/// <para>
/// What it expressly does <em>not</em> do: conclude from the shape of a word
/// that it is a skill. "Capitalised", "stands after the colon", "occurs often"
/// — every one of those rules would be a guess about a person, formed out of
/// the shape of their document. Whoever misses a word extends the vocabulary by
/// pull request; it is never learnt from people's documents (ADR-0023).
/// </para>
/// <para>
/// The price is honest and has to stay that way: <strong>what the vocabulary
/// does not know is not found.</strong> A surface that keeps quiet about it
/// silently claims a document holds nothing — ADR-0022 §3.
/// </para>
/// <para>
/// The result is a <em>suggestion</em> and never a naming. Between the word
/// that was found and a statement about the person lie two acts: a click fills
/// the form field, and only "Save" makes it a naming (ADR-0033).
/// </para>
/// </remarks>
public static class TermFinder
{
    /// <summary>From which length on a spelling counts in any casing.</summary>
    /// <remarks>
    /// <strong>Short abbreviations only in capitals, and that is measured.</strong>
    /// "wig" is a hairpiece in English, "go" an everyday verb, "ts" and "py"
    /// stand in every other file name. In lower case a three-letter word hits
    /// somewhere in every longer reference — and a wrong suggestion is more
    /// expensive here than a missing one, because it offers the person a word
    /// about themselves that never stood in their document.
    /// <para>
    /// The rule applies only to spellings made of <em>pure letters</em>: "C#",
    /// "C++" and "k8s" carry a character that makes them unmistakable anyway,
    /// and are still recognised in any casing.
    /// </para>
    /// </remarks>
    public const int UpperCaseOnlyBelowLength = 4;

    /// <summary>Spelling → canonical name, looked up in lower case.</summary>
    /// <remarks>
    /// Built from <see cref="Vocabulary.Spellings"/> and not maintained
    /// beside it: a second list would be the one to go stale first — and nobody
    /// would notice, because a word that is not found looks like a document
    /// with no content.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> BySpelling = Build();

    /// <summary>The longest spelling — the upper bound of every comparison window.</summary>
    private static readonly int LongestSpelling =
        BySpelling.Keys.Max(spelling => spelling.Length);

    /// <summary>The canonical names that occur in this text.</summary>
    /// <param name="text">The full text that was found. It is held nowhere.</param>
    /// <returns>
    /// Every name once, in the order of its first occurrence in the text. The
    /// order arranges <em>words</em> in <em>one document</em> and never people;
    /// nowhere does a number come out of it.
    /// </returns>
    public static IReadOnlyList<string> Find(string? text)
    {
        var condensed = Condense(text);

        if (condensed.Length == 0)
        {
            return [];
        }

        var lower = condensed.ToLowerInvariant();
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var start = 0; start < lower.Length; start++)
        {
            if (!StartsAWord(lower, start))
            {
                continue;
            }

            // From the longest window downwards: "Microsoft SQL Server" has to
            // win against "MySQL", and "gesundheits- und krankenpfleger"
            // against every shorter piece inside it. The other way round the
            // first partial word would win and the longer name would never get
            // its turn.
            var longest = Math.Min(LongestSpelling, lower.Length - start);

            for (var length = longest; length > 0; length--)
            {
                if (!EndsAWord(lower, start + length))
                {
                    continue;
                }

                var window = lower.Substring(start, length);

                if (!BySpelling.TryGetValue(window, out var name))
                {
                    continue;
                }

                if (length < UpperCaseOnlyBelowLength
                    && window.All(char.IsLetter)
                    && !IsUpperCase(condensed, start, length))
                {
                    continue;
                }

                if (seen.Add(name))
                {
                    found.Add(name);
                }

                // Over the hit, not into the middle of it: "MIG/MAG" must not
                // count a second time as a constituent of itself.
                start += length - 1;
                break;
            }
        }

        return found;
    }

    /// <summary>A character that does not continue a word — or the edge of the text.</summary>
    /// <remarks>
    /// The word boundary is the difference between "SPS" in "SPS-Programmierung"
    /// and "sps" somewhere inside another word. A hyphen is a boundary, a letter
    /// is not.
    /// </remarks>
    private static bool StartsAWord(string text, int position) =>
        position == 0 || !char.IsLetterOrDigit(text[position - 1]);

    private static bool EndsAWord(string text, int position) =>
        position >= text.Length || !char.IsLetterOrDigit(text[position]);

    /// <summary>Does this piece stand in the text without a single lower-case letter?</summary>
    private static bool IsUpperCase(string text, int start, int length)
    {
        for (var position = start; position < start + length; position++)
        {
            if (char.IsLower(text[position]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Every run of whitespace becomes a single space.
    /// </summary>
    /// <remarks>
    /// Without it "Erste Hilfe" would not find itself as soon as a line break
    /// stands between the two words — and in a typeset document one eventually
    /// stands between every pair of words.
    /// </remarks>
    private static string Condense(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var whitespace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                whitespace = true;
                continue;
            }

            if (whitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            whitespace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    private static FrozenDictionary<string, string> Build()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, spellings) in Vocabulary.Spellings)
        {
            // The canonical name points at itself: whoever writes "Kubernetes"
            // has written Kubernetes.
            table[name.ToLowerInvariant()] = name;

            foreach (var spelling in spellings)
            {
                table[spelling.ToLowerInvariant()] = name;
            }
        }

        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
