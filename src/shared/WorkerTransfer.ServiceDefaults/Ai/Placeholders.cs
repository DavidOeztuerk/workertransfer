using System.Text.RegularExpressions;

namespace WorkerTransfer.ServiceDefaults.Ai;

/// <summary>
/// Known values go to a model as placeholders and come back as themselves
/// (docs/VISION-FRAGEN.md, E2).
/// </summary>
/// <remarks>
/// <para><strong>Only what is known can be masked.</strong> A name, an
/// employer, a salary the platform holds in a field is replaced wherever it
/// occurs — also inside free text the person wrote. What the person typed
/// about somebody else in free text is not found; there the choice of
/// provider protects, not this class. Do not sell it as more.</para>
///
/// <para><strong>Whole words only.</strong> "Li" as a surname must not turn
/// "Lieferant" into "[PERSON_NAME_PART_2]eferant" — that would garble the
/// prompt and, restored, the answer.</para>
///
/// <para>One instance per call. It holds plain values and must never be
/// logged, cached or kept beyond the answer.</para>
/// </remarks>
public sealed partial class Placeholders
{
    private readonly List<Entry> _entries = [];

    /// <summary>The tokens handed out so far, in order.</summary>
    public IReadOnlyList<string> Tokens => _entries.Select(entry => entry.Token).ToArray();

    /// <summary>
    /// Registers a value and returns its token; the same value gets the same
    /// token. An empty value is returned unchanged and registers nothing.
    /// </summary>
    /// <param name="kind">Upper-case letters and underscores, e.g. <c>EMPLOYER</c>.</param>
    /// <param name="value">The real value.</param>
    /// <param name="numbered">
    /// Whether several values of this kind may exist (<c>EMPLOYER_1</c>,
    /// <c>EMPLOYER_2</c>) or just one (<c>PERSON_NAME</c>).
    /// </param>
    public string Add(string kind, string value, bool numbered = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(value);

        if (!KindShape().IsMatch(kind))
        {
            throw new ArgumentException("A kind is upper-case letters and underscores.", nameof(kind));
        }

        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return value;
        }

        var known = _entries.FirstOrDefault(entry =>
            string.Equals(entry.Value, trimmed, StringComparison.OrdinalIgnoreCase));

        if (known is not null)
        {
            return known.Token;
        }

        var token = numbered
            ? $"[{kind}_{_entries.Count(entry => entry.Kind == kind) + 1}]"
            : $"[{kind}]";

        if (_entries.Any(entry => entry.Token == token))
        {
            throw new InvalidOperationException($"{token} is already taken by another value.");
        }

        _entries.Add(new Entry(kind, token, trimmed, WholeWord(trimmed)));
        return token;
    }

    /// <summary>
    /// Registers a person's name: the whole name, and each part of three or
    /// more letters on its own — "Ich bin Anna" in the free text names her too.
    /// </summary>
    public string AddPersonName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var token = Add("PERSON_NAME", name);
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return token;
        }

        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length >= 3)
            {
                Add($"PERSON_NAME_PART_{index + 1}", parts[index]);
            }
        }

        return token;
    }

    /// <summary>Replaces every known value in <paramref name="text"/> by its token.</summary>
    /// <remarks>
    /// Longest value first: "Anna Muster" must become <c>[PERSON_NAME]</c>, not
    /// <c>[PERSON_NAME_PART_1] [PERSON_NAME_PART_2]</c>.
    /// </remarks>
    public string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var entry in _entries.OrderByDescending(entry => entry.Value.Length))
        {
            text = entry.Pattern.Replace(text, entry.Token);
        }

        return text;
    }

    /// <summary>Puts the real values back into a finished answer.</summary>
    /// <remarks>
    /// Case-insensitive and tolerant of spaces inside the brackets: a model
    /// that writes <c>[ Person_Name ]</c> meant the same token.
    /// </remarks>
    public string Restore(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return AnyToken().Replace(text, match =>
        {
            var inner = match.Groups["inner"].Value.Trim();
            var entry = _entries.FirstOrDefault(entry =>
                string.Equals(entry.Token, $"[{inner}]", StringComparison.OrdinalIgnoreCase));

            return entry?.Value ?? match.Value;
        });
    }

    /// <summary>
    /// Restores a fragment of a streamed answer and drops an unfinished token
    /// at its end — otherwise the person watches "[EMPLO" flicker into
    /// "Siemens".
    /// </summary>
    public string RestoreFragment(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var open = fragment.LastIndexOf('[');

        if (open >= 0 && fragment.IndexOf(']', open) < 0 && fragment.Length - open <= 40)
        {
            fragment = fragment[..open];
        }

        return Restore(fragment);
    }

    private static Regex WholeWord(string value) =>
        new($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*$")]
    private static partial Regex KindShape();

    [GeneratedRegex(@"\[(?<inner>\s*[A-Za-z][A-Za-z0-9_]*\s*)\]")]
    private static partial Regex AnyToken();

    private sealed record Entry(string Kind, string Token, string Value, Regex Pattern);
}
