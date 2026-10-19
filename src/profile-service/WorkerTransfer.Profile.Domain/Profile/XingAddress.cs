using System.Text.RegularExpressions;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>The address of a person's own XING profile (VISION-FRAGEN-2 N2).</summary>
/// <remarks>
/// <para><strong>Only a XING profile, and stored in one shape.</strong> The
/// page renders it as a link, so any other address would be a link a person
/// can place in front of every company that reads the profile. Query and
/// fragment are dropped; what remains is
/// <c>https://www.xing.com/profile/&lt;name&gt;</c>.</para>
///
/// <para><strong>It names the person.</strong> Everywhere else the civil name
/// is released last (ADR-0037 stage 3, ADR-0038); a XING profile shows it at
/// once. That is the person's choice, and the form says so before saving —
/// which is why the link travels with the full profile only, never in the
/// scout's hits.</para>
/// </remarks>
public static partial class XingAddress
{
    /// <summary>The one host the link may point to.</summary>
    public const string Host = "www.xing.com";

    /// <summary>The address in its stored shape, or <c>null</c> for "none".</summary>
    /// <exception cref="XingError">It is not a XING profile address.</exception>
    public static string? Normalize(string? address)
    {
        var trimmed = (address ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 200
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)
                 || uri.Host.Equals("xing.com", StringComparison.OrdinalIgnoreCase))
            || ProfilePath().Match(uri.AbsolutePath) is not { Success: true } match)
        {
            throw new XingError();
        }

        return $"https://{Host}/profile/{match.Groups["name"].Value}";
    }

    [GeneratedRegex("^/profile/(?<name>[A-Za-z0-9_-]{1,100})/?$")]
    private static partial Regex ProfilePath();
}
