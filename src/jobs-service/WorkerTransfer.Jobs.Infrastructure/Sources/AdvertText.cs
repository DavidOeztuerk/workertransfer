using System.Net;
using System.Text.RegularExpressions;

namespace WorkerTransfer.Jobs.Infrastructure.Sources;

/// <summary>Turns an ATS's HTML into the plain text an advert carries here.</summary>
/// <remarks>
/// The platform shows no foreign HTML: paragraphs and list items become line
/// breaks, every tag goes, entities are decoded. What a company styled at its
/// ATS is lost on purpose — a script in an advert would run in every reader's
/// browser.
/// </remarks>
public static partial class AdvertText
{
    /// <summary>Plain text from HTML — which may itself arrive HTML-escaped (Greenhouse).</summary>
    public static string FromHtml(string? html)
    {
        var text = WebUtility.HtmlDecode(html ?? string.Empty);

        // Greenhouse escapes its HTML once more: decoded once, it is HTML.
        if (text.Contains("&lt;", StringComparison.Ordinal))
        {
            text = WebUtility.HtmlDecode(text);
        }

        text = Blocks().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = Breaks().Replace(text, "\n\n");

        return string.Join('\n', text.Split('\n').Select(line => line.Trim())).Trim();
    }

    [GeneratedRegex(@"<\s*(br|/p|/li|/h[1-6]|/div|/ul|/ol)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Blocks();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n(\s*\n)+")]
    private static partial Regex Breaks();
}
